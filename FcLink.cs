using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FcTimers;

// Ligação por código (contrato em Plugin FF\plano\CONTRATO-LIGACAO.md): quem gerencia o servidor gera o código
// no /fcc do Discord, e cada membro cola o código aqui e confirma a FC. O serviço devolve um token, que entra
// no lugar da chave da FC. A ligação é da instalação (o dono), não de cada personagem.
public sealed class FcLink(Configuration config, Action save, Action<string> log, Action<string> notify) : IDisposable
{
    // Serviço da FC Companion. A configuração ainda aceita outro endereço, para quem roda o próprio.
    public const string DefaultServiceUrl = "https://fc-timers.lucianobentrup.workers.dev";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient http = NetHttp.Create(TimeSpan.FromSeconds(20));

    public sealed record Check(string Code, string? FcName, string? ServerName, bool FirstLink);

    private sealed record CheckReply(string Code, string? FcName, string? ServerName, bool FirstLink, string? Error);

    private sealed record ConfirmReply(string? Token, string? FcName, string? ServerName, string? Status, string? Role, string? Error);

    private sealed record MeReply(string? Status, string? Role, string? FcName, int Pending);

    private sealed record MembersReply(List<Member>? Members, string? Error);

    public sealed record Member(string Id, string? Name, string? World, string Role, string Status, long RequestedAt, long? JoinedAt, bool You);

    private static readonly TimeSpan MeInterval = TimeSpan.FromMinutes(5);
    private DateTime nextMe = DateTime.MinValue;

    public bool Pending => Linked && config.LinkStatus == "pending";

    public bool Manager => Linked && config.LinkStatus != "pending" && config.LinkRole is "owner" or "admin";

    public bool IsOwner => Linked && config.LinkRole == "owner";

    // Pedidos esperando resposta, para o dono e os admins.
    public int PendingRequests { get; private set; }

    public List<Member> Members { get; private set; } = [];

    private sealed record AccountReply(bool Ok, string? Error);

    public static bool IsToken(string key) => key.StartsWith("fco_", StringComparison.Ordinal);

    public bool Linked => IsToken(config.ServiceKey);

    public bool Busy { get; private set; }

    // Mensagem do último passo, para a tela mostrar ("Código não encontrado", "Ligado!"...).
    public string Status
    {
        get => DateTime.UtcNow - statusAt < StatusShown ? status : string.Empty;
        private set
        {
            status = value;
            statusAt = DateTime.UtcNow;
        }
    }

    private static readonly TimeSpan StatusShown = TimeSpan.FromSeconds(10);
    private string status = string.Empty;
    private DateTime statusAt;

    // Resultado do /link/check, esperando o "sim" na tela.
    public Check? Checked => pending;

    private Check? pending;

    public string ServiceUrl => (config.ServiceUrl.Trim().Length > 0 ? config.ServiceUrl.Trim() : DefaultServiceUrl).TrimEnd('/');

    public void Dispose() => http.Dispose();

    public void CancelPending()
    {
        pending = null;
        Status = string.Empty;
    }

    public void CheckCode(string code, ulong fcId) => Run(async () =>
    {
        var (status, reply) = await Post<CheckReply>("/link/check", new { code, fcId = fcId.ToString(), owner = config.OwnerId });
        if (status == HttpStatusCode.OK && reply != null)
        {
            pending = new Check(reply.Code, reply.FcName, reply.ServerName, reply.FirstLink);
            Status = string.Empty;
            return;
        }

        Status = Error(status, reply?.Error, reply?.FcName);
    });

    // characters: todos os personagens conhecidos com ID de FC. O serviço guarda só os da mesma FC.
    public void Confirm(ulong fcId, string fcName, CharacterRecord? requester, IEnumerable<CharacterRecord> characters) => Run(async () =>
    {
        if (this.pending is not { } pending)
            return;

        var list = characters.Where(c => c.FcId != 0).Select(c => new { name = c.Name, world = c.World, fcId = c.FcId.ToString() }).ToList();
        var (status, reply) = await Post<ConfirmReply>("/link/confirm", new
        {
            code = pending.Code, fcId = fcId.ToString(), fcName, owner = config.OwnerId, characters = list,
            requester = requester == null ? null : new { name = requester.Name, world = requester.World },
        });
        if (status == HttpStatusCode.OK && reply?.Token is { Length: > 0 } token)
        {
            config.ServiceKey = token;
            config.ServiceUrl = ServiceUrl;
            config.SyncEnabled = true;
            config.LinkedFcName = reply.FcName ?? fcName;
            config.LinkedServerName = reply.ServerName ?? pending.ServerName ?? string.Empty;
            config.LinkStatus = reply.Status ?? "member";
            config.LinkRole = reply.Role ?? "member";
            save();
            this.pending = null;
            Status = config.LinkStatus == "pending" ? L.LinkRequested : L.LinkDone;
            log(config.LinkStatus == "pending" ? $"Pedido para entrar na FC {config.LinkedFcName} enviado" : $"Plugin ligado à FC {config.LinkedFcName}");
            return;
        }

        Status = Error(status, reply?.Error, reply?.FcName);
    });

    // Código de 6 dígitos que o /fcc do Discord mostra em "Ligar minha conta".
    public void LinkAccount(string code) => Run(async () =>
    {
        var (status, reply) = await Post<AccountReply>("/link/account", new { code }, config.ServiceKey);
        Status = status == HttpStatusCode.OK && reply?.Ok == true ? L.AccountLinked : Error(status, reply?.Error, null);
        if (status == HttpStatusCode.OK)
            log("Conta do Discord ligada a este plugin");
    });

    public void Leave() => Run(async () =>
    {
        var (status, _) = await Post<AccountReply>("/link/leave", new { }, config.ServiceKey);
        if (status is HttpStatusCode.OK or HttpStatusCode.Unauthorized)
        {
            config.ServiceKey = string.Empty;
            config.LinkedFcName = string.Empty;
            config.LinkedServerName = string.Empty;
            config.LinkStatus = string.Empty;
            config.LinkRole = string.Empty;
            save();
            Status = L.LinkLeft;
            log("Plugin desligado da FC");
            return;
        }

        Status = Error(status, null, null);
    });

    // A cada 5 minutos: situação do pedido, papel e pedidos novos. Avisa quando o pedido é aceito,
    // quando chega pedido para o dono ou um admin, e quando o plugin foi tirado da FC.
    public void Tick()
    {
        if (!Linked || Busy || DateTime.UtcNow < nextMe)
            return;

        nextMe = DateTime.UtcNow + MeInterval;
        RefreshNow();
    }

    public void RefreshNow() => Run(async () =>
    {
        var (status, reply) = await Post<MeReply>("/link/me", new { }, config.ServiceKey);
        if (status != HttpStatusCode.OK || reply == null)
        {
            Status = string.Empty;
            return;
        }

        if (reply.Status == null)
        {
            // Token que não vale mais: recusado ou expulso.
            config.ServiceKey = string.Empty;
            config.LinkStatus = string.Empty;
            config.LinkRole = string.Empty;
            save();
            notify(L.LinkRemoved);
            log("Este plugin saiu da FC (pedido recusado ou expulso)");
            Status = string.Empty;
            return;
        }

        if (config.LinkStatus == "pending" && reply.Status == "member")
            notify(L.LinkAccepted);
        if (reply.Pending > PendingRequests && reply.Role is "owner" or "admin")
            notify(L.NewRequests(reply.Pending));

        PendingRequests = reply.Pending;
        if (config.LinkStatus != reply.Status || config.LinkRole != reply.Role)
        {
            config.LinkStatus = reply.Status;
            config.LinkRole = reply.Role ?? "member";
            save();
        }

        Status = string.Empty;
    }, quiet: true);

    // TESTE: cria um pedido falso para a aba Gerenciar. O serviço só aceita com TEST_TOOLS ligado.
    public void TestRequest() => Run(async () =>
    {
        if (!Linked)
        {
            Status = L.TestNeedsLink;
            return;
        }

        var (code, _) = await Post<AccountReply>("/link/test-request", new { }, config.ServiceKey);
        Status = code == HttpStatusCode.OK ? L.TestRequestMade : code == HttpStatusCode.NotFound ? L.TestNeedsManager : L.LinkFailed((int)code);
        nextMe = DateTime.MinValue;
    });

    public void LoadMembers() => Run(async () =>
    {
        var (status, reply) = await Post<MembersReply>("/link/members", new { }, config.ServiceKey);
        if (status == HttpStatusCode.OK && reply?.Members != null)
        {
            Members = reply.Members;
            PendingRequests = Members.Count(m => m.Status == "pending");
            Status = string.Empty;
            return;
        }

        Status = Error(status, reply?.Error, null);
    }, quiet: true);

    // accept, reject, kick, promote ou demote. Recarrega a lista no fim.
    public void Manage(string action, string member) => Run(async () =>
    {
        var (status, reply) = await Post<AccountReply>("/link/manage", new { action, member }, config.ServiceKey);
        if (status != HttpStatusCode.OK || reply?.Ok != true)
        {
            Status = Error(status, reply?.Error, null);
            return;
        }

        log($"Gerenciamento: {action}");
        var (_, list) = await Post<MembersReply>("/link/members", new { }, config.ServiceKey);
        Members = list?.Members ?? Members;
        PendingRequests = Members.Count(m => m.Status == "pending");
        Status = string.Empty;
    });

    private void Run(Func<Task> work, bool quiet = false)
    {
        if (Busy)
            return;

        Busy = true;
        if (!quiet)
            Status = L.LinkWorking;
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                Status = L.LinkOffline;
                log($"Ligação: {e.GetType().Name}");
            }
            finally
            {
                Busy = false;
            }
        });
    }

    private async Task<(HttpStatusCode Status, T? Reply)> Post<T>(string path, object body, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ServiceUrl + path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        T? reply = default;
        if (text.Length > 0 && text.TrimStart().StartsWith('{'))
            reply = JsonSerializer.Deserialize<T>(text, ReadOptions);
        return (response.StatusCode, reply);
    }

    private static string Error(HttpStatusCode status, string? error, string? fcName) => (status, error) switch
    {
        (_, "unknown-code") => L.LinkUnknownCode,
        (_, "other-fc") => L.LinkOtherFc(fcName ?? "?"),
        (_, "too-many") => L.LinkTooMany,
        (_, "invalid") => L.LinkInvalid,
        (HttpStatusCode.Unauthorized, _) => L.LinkExpired,
        _ => L.LinkFailed((int)status),
    };
}
