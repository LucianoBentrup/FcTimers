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
public sealed class FcLink(Configuration config, Action save, Action<string> log) : IDisposable
{
    // Serviço da FC Companion. A configuração ainda aceita outro endereço, para quem roda o próprio.
    public const string DefaultServiceUrl = "https://fc-timers.lucianobentrup.workers.dev";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record Check(string Code, string? FcName, string? ServerName, bool FirstLink);

    private sealed record CheckReply(string Code, string? FcName, string? ServerName, bool FirstLink, string? Error);

    private sealed record ConfirmReply(string? Token, string? FcName, string? ServerName, string? Error);

    private sealed record AccountReply(bool Ok, string? Error);

    public static bool IsToken(string key) => key.StartsWith("fco_", StringComparison.Ordinal);

    public bool Linked => IsToken(config.ServiceKey);

    public bool Busy { get; private set; }

    // Mensagem do último passo, para a tela mostrar ("Código não encontrado", "Ligado!"...).
    public string Status { get; private set; } = string.Empty;

    // Resultado do /link/check, esperando o "sim" na tela.
    public Check? Pending { get; private set; }

    public string ServiceUrl => (config.ServiceUrl.Trim().Length > 0 ? config.ServiceUrl.Trim() : DefaultServiceUrl).TrimEnd('/');

    public void Dispose() => http.Dispose();

    public void CancelPending()
    {
        Pending = null;
        Status = string.Empty;
    }

    public void CheckCode(string code, ulong fcId) => Run(async () =>
    {
        var (status, reply) = await Post<CheckReply>("/link/check", new { code, fcId = fcId.ToString(), owner = config.OwnerId });
        if (status == HttpStatusCode.OK && reply != null)
        {
            Pending = new Check(reply.Code, reply.FcName, reply.ServerName, reply.FirstLink);
            Status = string.Empty;
            return;
        }

        Status = Error(status, reply?.Error, reply?.FcName);
    });

    // characters: todos os personagens conhecidos com ID de FC. O serviço guarda só os da mesma FC.
    public void Confirm(ulong fcId, string fcName, IEnumerable<CharacterRecord> characters) => Run(async () =>
    {
        if (Pending is not { } pending)
            return;

        var list = characters.Where(c => c.FcId != 0).Select(c => new { name = c.Name, world = c.World, fcId = c.FcId.ToString() }).ToList();
        var (status, reply) = await Post<ConfirmReply>("/link/confirm", new { code = pending.Code, fcId = fcId.ToString(), fcName, owner = config.OwnerId, characters = list });
        if (status == HttpStatusCode.OK && reply?.Token is { Length: > 0 } token)
        {
            config.ServiceKey = token;
            config.ServiceUrl = ServiceUrl;
            config.SyncEnabled = true;
            config.LinkedFcName = reply.FcName ?? fcName;
            config.LinkedServerName = reply.ServerName ?? pending.ServerName ?? string.Empty;
            save();
            Pending = null;
            Status = L.LinkDone;
            log($"Plugin ligado à FC {config.LinkedFcName} pelo código");
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
            save();
            Status = L.LinkLeft;
            log("Plugin desligado da FC");
            return;
        }

        Status = Error(status, null, null);
    });

    private void Run(Func<Task> work)
    {
        if (Busy)
            return;

        Busy = true;
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
