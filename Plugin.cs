using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace FcTimers;

public sealed partial class Plugin : IDalamudPlugin
{
    private const string Command = "/fcc";
    // Nomes antigos: ainda funcionam, mas somem da ajuda. Saem numa versão depois da 0.10.1.
    private static readonly string[] OldCommands = ["/fct", "/fctimers"];
    private const int ActiveBuffStableSeconds = 5;
    private const int ActiveBuffSettleSeconds = 60;
    private const int MaxCompanySlots = 3;
    private const int AirshipLootSeen = 100;
    public const long HouseLimitSeconds = 45 * 86400;
    private static readonly int[] HouseWarningDays = [30, 40, 44];
    private const long HouseVisitStep = 600;
    private const long HouseVisitLogGap = 6 * 3600;

    // Mensagens do jogo sobre a horta. A 4017 ("This crop is doing well.") aparece ao cuidar da planta,
    // e a 4015 ao plantar. Ainda falta confirmar no jogo que só aparecem nesses momentos.
    private const uint CropTendedMessage = 4017;
    private const uint SeedsPlantedMessage = 4015;

    // Nos vasos de dentro de casa, cuidar da planta mostra outra mensagem ("The crops appear to be healthy.").
    private const uint PotTendedMessage = 3450;
    private const uint FirstGardenMessage = 4000;
    private const uint LastGardenMessage = 4026;
    private const string GeneralPlot = ConfigMigration.GeneralPlot;
    private const string GeneralPlotName = ConfigMigration.GeneralPlotName;

    // Telas que aparecem quando o resultado de uma viagem chega.
    private static readonly string[] ResultAddons = ["AirShipExplorationResult", "SubmersibleExplorationResult"];

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IDtrBar DtrBar { get; set; } = null!;
    [PluginService] private static IChatGui ChatGui { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IPlayerState PlayerState { get; set; } = null!;
    [PluginService] private static ICondition Condition { get; set; } = null!;
    [PluginService] private static ITargetManager TargetManager { get; set; } = null!;
    [PluginService] private static IAddonLifecycle AddonLifecycle { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static INotificationManager Notifications { get; set; } = null!;
    [PluginService] private static ITextureProvider Textures { get; set; } = null!;

    private readonly WindowSystem windowSystem = new("FcTimers");
    private readonly MainWindow mainWindow;
    private readonly ChangelogWindow changelogWindow;
    private readonly ConfigWindow configWindow;
    private readonly IDtrBarEntry dtrEntry;
    private readonly Dictionary<uint, string> companyStatuses;
    private readonly List<CompanyActionInfo> companyActions;
    private readonly LocalReadings localReadings = new();
    private DateTime nextUpdate = DateTime.MinValue;
    private string candidateBuffs = string.Empty;
    private int candidateSeconds;
    private int secondsSinceLogin;
    private bool fcIdWaiting;
    private HashSet<string> seenBuffs = [];
    private bool characterEnabled;
    // O personagem logado é de outra FC: não lê nem envia nada além da oficina, que é onde ele pode voltar a valer.
    private bool currentOtherFc;
    private bool inWorkshop;
    private string? lastIndoorHouse;
    private (string Key, string Name, bool Indoor, DateTime SeenAt)? lastHousingObject;
    private string lastEmptyPages = string.Empty;
    private RemoteState? appliedRemote;
    private string lastScan = string.Empty;
    private DateTime nextScan = DateTime.MinValue;
    private DateTime autoLogAt = DateTime.MaxValue;
    private DateTime firstQueuedLog = DateTime.MaxValue;
    private int uploadedLines;

    // Se o último personagem logado estava ativo. Vale depois de deslogar, para o log enviado ao fechar o jogo.
    private bool lastCharacterActive;

    public Configuration Config { get; }
    public SyncClient Sync { get; }
    public FcLink Link { get; }
    public DiagnosticLog Diagnostics { get; }
    public FcView View { get; private set; } = new();
    public ulong CurrentCharacterId { get; private set; }

    public static string Version => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "?";

    public string LanguageCode => Config.Language.Length > 0 ? Config.Language
        : PluginInterface.UiLanguage.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt" : "en";

    public Plugin()
    {
        var saved = PluginInterface.GetPluginConfig() as Configuration;
        Config = saved ?? new Configuration { LastChangelogVersion = Version };
        Migrate();
        L.English = LanguageCode == "en";
        GameIcons.Init(Textures, DataManager);
        if (Config.OwnerId.Length == 0)
        {
            Config.OwnerId = FcIdentity.NewOwnerId();
            SaveConfig();
        }
        Fonts.Init(PluginInterface.UiBuilder);

        Diagnostics = new DiagnosticLog(
            System.IO.Path.Combine(PluginInterface.GetPluginConfigDirectory(), "diagnostico.txt"),
            message => Log.Information(message),
            () => Config.DetailedLog);
        Diagnostics.Write($"Plugin {DisplayVersion} iniciado");

        Link = new FcLink(Config, SaveConfig, message => Diagnostics.Write(message));
        Sync = new SyncClient(Config, SaveConfig, Diagnostics.Write, IsMarketable, () => MarketWorld);
        Diagnostics.Write($"Itens do estoque: {LookupItem(Supplies.CeruleumTank)?.Name ?? "?"} e {LookupItem(Supplies.RepairMaterials)?.Name ?? "?"}");

        // No jogo, cada ação da FC é um status com o mesmo nome-base, sem o nível.
        companyActions = DataManager.GetExcelSheet<CompanyAction>()
            .Select(a => new CompanyActionInfo(a.RowId, a.Name.ToString(), a.Description.ToString()))
            .Where(a => a.Name.Length > 0)
            .ToList();
        var actionNames = companyActions.Select(a => a.Name).ToHashSet();
        companyStatuses = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>()
            .Where(s => actionNames.Contains(s.Name.ToString()))
            .ToDictionary(s => s.RowId, s => s.Name.ToString());

        mainWindow = new MainWindow(this);
        changelogWindow = new ChangelogWindow(PluginInterface.IsDev);
        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(changelogWindow);
        windowSystem.AddWindow(configWindow);

        // O changelog abre uma vez depois de cada atualização, nunca na primeira instalação.
        if (Config.LastChangelogVersion != Version)
        {
            changelogWindow.Show(Config.LastChangelogVersion);
            Config.LastChangelogVersion = Version;
            SaveConfig();
        }

        dtrEntry = DtrBar.Get("FC Companion");
        dtrEntry.OnClick = _ => mainWindow.Toggle();

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = L.HelpMain,
        });
        foreach (var old in OldCommands)
        {
            CommandManager.AddHandler(old, new CommandInfo(OnCommand)
            {
                HelpMessage = L.HelpMain,
                ShowInHelp = false,
            });
        }

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, ResultAddons, OnVoyageResult);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnSelectString);
        ChatGui.LogMessage += OnLogMessage;

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        ChatGui.LogMessage -= OnLogMessage;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", OnSelectString);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, ResultAddons, OnVoyageResult);
        foreach (var old in OldCommands)
            CommandManager.RemoveHandler(old);
        CommandManager.RemoveHandler(Command);
        dtrEntry.Remove();
        windowSystem.RemoveAllWindows();
        Link.Dispose();
        Fonts.Dispose();

        // Fechando o jogo logo depois de uma coleta: o log que ainda não foi enviado vai agora, esperando no máximo 3 segundos.
        if (autoLogAt != DateTime.MaxValue && Diagnostics.Written != uploadedLines && CanSendLog)
        {
            Diagnostics.Write("Enviando o log ao serviço (automático, plugin fechando)");
            Sync.SendLogNow(CurrentCharacterName(), LogText(), TimeSpan.FromSeconds(3));
        }

        Sync.Dispose();
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(Config);

    public bool CurrentCharacterEnabled => characterEnabled;

    // FC do personagem logado, para a ligação por código. 0: sem FC ou ID ainda não lido.
    public ulong CurrentFcId { get; private set; }
    public string CurrentFcTag { get; private set; } = string.Empty;
    public bool CurrentCharacterOtherFc => characterEnabled && currentOtherFc;

    // Carregado pelo Dev Plugin Locations do Dalamud, e não pelo repositório.
    public bool IsDevBuild => PluginInterface.IsDev;

    // A versão de dev se identifica no log, para não ser confundida com a publicada.
    public string DisplayVersion => IsDevBuild ? $"{Version}-dev" : Version;

    // Só na versão de dev: mostra as boas-vindas de novo, para testar.
    public void ReplayWelcome()
    {
        Config.OnboardingDone = false;
        SaveConfig();
        mainWindow.IsOpen = true;
    }

    public void OpenSettings() => configWindow.IsOpen = true;

    public void OpenChangelog() => changelogWindow.Show(null);

    public void EnableCurrentCharacter()
    {
        if (!Config.Characters.TryGetValue(CurrentCharacterId, out var character))
            return;

        character.Enabled = true;
        characterEnabled = true;
        SaveConfig();
    }

    // Pontinho no topo da janela: verde sincronizado, amarelo com problema, cinza desligado.
    public (System.Numerics.Vector4 Color, string Text) SyncState()
    {
        if (!Config.SyncEnabled || Config.ServiceKey.Length == 0)
            return (UiKit.Muted, L.SyncOff);
        if (Sync.LastFetch is { } fetched && DateTime.Now - fetched < TimeSpan.FromMinutes(10) && !Sync.Status.StartsWith("Erro") && !Sync.Status.StartsWith("Sem"))
            return (UiKit.Good, L.SyncOk($"{fetched:HH:mm}"));
        return (UiKit.Soon, L.SyncProblem);
    }

    // Aviso no jogo: notificação do Dalamud, chat e som, conforme as configurações.
    public void Notify(string message)
    {
        if (Config.AlertToast)
            Notifications.AddNotification(new Notification { Title = "FC Companion", Content = message, Type = NotificationType.Info });
        if (Config.AlertChat)
            ChatGui.Print(message, "FC Companion");
        if (Config.AlertSound)
            FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(Config.AlertSoundId, 1, 16));
    }

    public static string FormatRemaining(long seconds)
    {
        if (seconds <= 0)
            return L.Ready;

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h{time.Minutes:00}" : $"{time.Minutes}m{time.Seconds:00}";
    }

    private void Migrate()
    {
        ConfigMigration.Run(Config, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        SaveConfig();
    }

    private void OnCommand(string command, string arguments)
    {
        var argument = arguments.Trim().ToLowerInvariant();
        if (argument is "regar" or "water")
        {
            WaterAll($"comando {command} regar");
            ChatGui.Print(L.WateredAll, "FC Companion");
            return;
        }

        mainWindow.Toggle();
    }

    private void OnUpdate(IFramework framework)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Dentro da oficina, os subs são lidos a cada frame, para não perder o loot quando a coleta é rápida.
        if (characterEnabled)
            ReadSubmarines(now);

        if (DateTime.UtcNow < nextUpdate)
            return;
        nextUpdate = DateTime.UtcNow.AddSeconds(1);

        characterEnabled = IsCurrentCharacterEnabled();
        if (characterEnabled && !currentOtherFc)
        {
            ReadWheels(now);
            ReadActiveBuffs(now);
            ReadSupplies(now);
        }

        if (characterEnabled)
            CheckHouseVisit(now);
        else
            lastIndoorHouse = null;

        Sync.ReportSupplies(Config.Supplies.Tanks, Config.Supplies.Kits, Config.Supplies.ChangedAt);

        foreach (var (key, plot) in Config.Plots.Where(p => p.Value.WateredAt != 0))
        {
            Sync.ReportPlot(
                key,
                plot.Name,
                plot.WateredAt,
                plot.NextWaterAt,
                plot.DieAt,
                plot.Indoor);
        }

        if (DateTime.UtcNow >= autoLogAt && !Sync.SendingLog)
        {
            autoLogAt = DateTime.MaxValue;
            firstQueuedLog = DateTime.MaxValue;
            if (Diagnostics.Written != uploadedLines)
                SendLog("automático, depois de uma ação importante");
        }

        L.English = LanguageCode == "en";
        Sync.WindowOpen = mainWindow.IsOpen;
        Sync.Tick();
        if (Sync.Remote is { } remote && !ReferenceEquals(remote, appliedRemote))
        {
            appliedRemote = remote;
            ApplyRemoteFields(remote);
        }

        View = FcView.Build(Config, localReadings, Sync.Remote, Sync.RemoteOffset);
        CheckAlerts(now);
        UpdateDtr(now);
    }

    // Personagem novo entra na lista desmarcado, e só um personagem marcado lê e envia dados.
    private bool IsCurrentCharacterEnabled()
    {
        var player = ObjectTable.LocalPlayer;
        var id = PlayerState.ContentId;
        CurrentCharacterId = player == null ? 0 : id;
        if (player == null || id == 0)
            return false;

        var name = player.Name.TextValue;

        var home = player.HomeWorld.ValueNullable;
        if (home is { } world && (world.Name.ToString() != Config.HomeWorld || world.DataCenter.RowId != Config.HomeDataCenter))
        {
            Config.HomeWorld = world.Name.ToString();
            Config.HomeDataCenter = world.DataCenter.RowId;
            SaveConfig();
        }

        var worldName = home?.Name.ToString() ?? string.Empty;
        if (!Config.Characters.TryGetValue(id, out var character))
        {
            character = new CharacterRecord { Name = name, World = worldName };
            Config.Characters[id] = character;
            SaveConfig();
        }
        else if (character.Name != name || (worldName.Length > 0 && character.World != worldName))
        {
            character.Name = name;
            character.World = worldName.Length > 0 ? worldName : character.World;
            SaveConfig();
        }

        var tag = player.CompanyTag.TextValue;
        if (character.FcTag != tag)
        {
            character.FcTag = tag;
            SaveConfig();
        }

        ReadFcId(character, tag, player.CurrentWorld.RowId == player.HomeWorld.RowId);

        // Personagem de outra FC: o ID da FC (ou, sem ele, a tag) diferente da FC do painel basta, sem precisar entrar na oficina.
        // Com o ID igual, o personagem volta a valer; a tag sozinha nunca libera, porque pode coincidir.
        var other = FcIdentity.IsOtherFc(character, Config);
        if (character.Enabled && other == true && !character.OtherFc)
        {
            character.OtherFc = true;
            SaveConfig();
            Diagnostics.Write($"{name} é de outra FC (ID {character.FcId}, «{tag}»; FC do painel: ID {Config.FcId}, «{Config.FcTag}»). Nada deste personagem é lido nem enviado.");
        }
        else if (other == false && character.OtherFc)
        {
            character.OtherFc = false;
            SaveConfig();
            Diagnostics.Write($"{name} tem o mesmo ID de FC do painel ({Config.FcId}): volta a ler e enviar dados");
        }

        currentOtherFc = character.OtherFc;
        CurrentFcId = character.FcId;
        CurrentFcTag = tag;
        Sync.FcId = character.FcId;

        // Guardado só para personagem ativo: é o nome do log enviado depois de deslogar.
        lastCharacterActive = character.Enabled;
        if (character.Enabled && Config.LastCharacterName != name)
        {
            Config.LastCharacterName = name;
            SaveConfig();
        }

        return character.Enabled;
    }

    // O ID da FC só vale no mundo de origem e pode demorar uns segundos depois do login.
    // Com tag e ID 0, ainda não carregou: espera o próximo segundo. Sem tag, o personagem não tem FC.
    private unsafe void ReadFcId(CharacterRecord character, string tag, bool homeWorld)
    {
        if (!homeWorld)
            return;

        var proxy = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyFreeCompany.Instance();
        var id = proxy == null ? 0 : proxy->Id;
        if (tag.Length > 0 && id == 0)
        {
            if (!fcIdWaiting)
                Diagnostics.Write($"{character.Name}: tag «{tag}», mas o ID da FC ainda não carregou");
            fcIdWaiting = true;
            return;
        }

        if (fcIdWaiting)
            Diagnostics.Write($"{character.Name}: ID da FC carregou {secondsSinceLogin} s depois do login");
        fcIdWaiting = false;

        if (character.FcId == id)
            return;

        Diagnostics.Write($"{character.Name}: ID da FC {(id == 0 ? "nenhum (sem FC)" : id.ToString())}");
        character.FcId = id;
        SaveConfig();
    }

    public string LogText()
        => Diagnostics.Text($"FC Companion {DisplayVersion} | fuso UTC{DateTimeOffset.Now:zzz} | gerado {DateTime.Now:dd/MM HH:mm}");

    // Depois de deslogar (o log que vai ao fechar o jogo), vale o último personagem que estava jogando.
    private string CurrentCharacterName()
        => Config.Characters.TryGetValue(CurrentCharacterId, out var character) ? character.Name
            : Config.LastCharacterName.Length > 0 ? Config.LastCharacterName : "Desconhecido";

    // Só personagem ativo manda log, seja automático, pelo botão ou ao fechar o jogo.
    public bool CanSendLog => characterEnabled || (CurrentCharacterId == 0 && lastCharacterActive);

    public void SendLog(string source)
    {
        if (!CanSendLog || !Sync.SendLog(CurrentCharacterName(), LogText()))
            return;

        Diagnostics.Write($"Enviando o log ao serviço ({source})");
        uploadedLines = Diagnostics.Written;
    }

    // Depois de uma ação importante (loot salvo ou perdido, sub saindo, rega, estoque, peças), o log vai sozinho
    // ao serviço 20 segundos depois, para pegar o que vier em seguida. Ações em sequência entram no mesmo envio,
    // que nunca espera mais de 2 minutos.
    private void QueueLogUpload()
    {
        var now = DateTime.UtcNow;
        if (firstQueuedLog == DateTime.MaxValue)
            firstQueuedLog = now;
        autoLogAt = now - firstQueuedLog < TimeSpan.FromMinutes(2) ? now.AddSeconds(20) : autoLogAt;
    }

    public string MarketWorld => Config.MarketWorld.Length > 0 ? Config.MarketWorld : Config.HomeWorld;

    // Mundos públicos do data center do personagem, para escolher onde ver os preços.
    public List<string> MarketWorlds()
    {
        var worlds = DataManager.GetExcelSheet<World>()
            .Where(w => w.IsPublic && w.DataCenter.RowId == Config.HomeDataCenter && Config.HomeDataCenter != 0)
            .Select(w => w.Name.ToString()).Order().ToList();
        if (DataManager.GetExcelSheet<WorldDCGroupType>().GetRowOrDefault(Config.HomeDataCenter) is { } dataCenter)
            worlds.Add(dataCenter.Name.ToString());
        return worlds;
    }

    private static bool IsMarketable(uint itemId)
        => DataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } item && !item.IsUntradable && item.ItemSearchCategory.RowId != 0;

    private static (string Name, uint Price)? LookupItem(uint itemId)
    {
        var item = DataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        return item == null ? null : (item.Value.Name.ToString(), item.Value.PriceLow);
    }
}
