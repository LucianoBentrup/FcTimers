using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace FcTimers;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/fctimers";
    private const string ShortCommand = "/fct";
    private const int ActiveBuffStableSeconds = 5;
    private const int ActiveBuffSettleSeconds = 60;
    private const int MaxCompanySlots = 3;

    // Mensagens do jogo sobre a horta. A 4017 ("This crop is doing well.") aparece ao cuidar da planta,
    // e a 4015 ao plantar. Ainda falta confirmar no jogo que só aparecem nesses momentos.
    private const uint CropTendedMessage = 4017;
    private const uint SeedsPlantedMessage = 4015;
    private const uint FirstGardenMessage = 4000;
    private const uint LastGardenMessage = 4026;
    private const string GeneralPlot = "geral";
    private const string GeneralPlotName = "Plantação";

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

    private readonly WindowSystem windowSystem = new("FcTimers");
    private readonly MainWindow mainWindow;
    private readonly ChangelogWindow changelogWindow;
    private readonly IDtrBarEntry dtrEntry;
    private readonly Dictionary<uint, string> companyStatuses;
    private readonly List<CompanyActionInfo> companyActions;
    private readonly LocalReadings localReadings = new();
    private DateTime nextUpdate = DateTime.MinValue;
    private string candidateBuffs = string.Empty;
    private int candidateSeconds;
    private int secondsSinceLogin;
    private HashSet<string> seenBuffs = [];
    private bool characterEnabled;
    private bool inWorkshop;
    private (string Key, string Name, bool Indoor, DateTime SeenAt)? lastHousingObject;
    private string lastEmptyPages = string.Empty;
    private DateTime gardenWatchUntil = DateTime.MinValue;
    private string lastScan = string.Empty;
    private DateTime nextScan = DateTime.MinValue;
    private DateTime autoLogAt = DateTime.MaxValue;
    private DateTime firstQueuedLog = DateTime.MaxValue;
    private int uploadedLines;

    public Configuration Config { get; }
    public SyncClient Sync { get; }
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

        Diagnostics = new DiagnosticLog(
            System.IO.Path.Combine(PluginInterface.GetPluginConfigDirectory(), "diagnostico.txt"),
            message => Log.Information(message),
            () => Config.DetailedLog);
        Diagnostics.Write($"Plugin {Version} iniciado");

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
        changelogWindow = new ChangelogWindow();
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(changelogWindow);

        // O changelog abre uma vez depois de cada atualização, nunca na primeira instalação.
        if (Config.LastChangelogVersion != Version)
        {
            changelogWindow.IsOpen = true;
            Config.LastChangelogVersion = Version;
            SaveConfig();
        }

        dtrEntry = DtrBar.Get("FC Timers");
        dtrEntry.OnClick = _ => mainWindow.Toggle();

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = L.HelpMain,
        });
        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = L.HelpShort,
        });

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, ResultAddons, OnVoyageResult);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnSelectString);
        ChatGui.LogMessage += OnLogMessage;
        ChatGui.ChatMessageUnhandled += OnChatMessage;

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += mainWindow.Toggle;
        Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        ChatGui.LogMessage -= OnLogMessage;
        ChatGui.ChatMessageUnhandled -= OnChatMessage;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", OnSelectString);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, ResultAddons, OnVoyageResult);
        CommandManager.RemoveHandler(ShortCommand);
        CommandManager.RemoveHandler(Command);
        dtrEntry.Remove();
        windowSystem.RemoveAllWindows();

        // Fechando o jogo logo depois de uma coleta: o log que ainda não foi enviado vai agora, esperando no máximo 3 segundos.
        if (autoLogAt != DateTime.MaxValue && Diagnostics.Written != uploadedLines)
        {
            Diagnostics.Write("Enviando o log ao serviço (automático, plugin fechando)");
            Sync.SendLogNow(CurrentCharacterName(), LogText(), TimeSpan.FromSeconds(3));
        }

        Sync.Dispose();
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(Config);

    public static string FormatRemaining(long seconds)
    {
        if (seconds <= 0)
            return L.Ready;

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h{time.Minutes:00}" : $"{time.Minutes}m{time.Seconds:00}";
    }

    private void Migrate()
    {
        if (Config.Version < 4)
        {
            Config.Voyages = LootLedger.MigrateKeys(Config.Voyages);
            Config.Version = 4;
        }

        // Até a versão 0.6.0 a 5ª posição era lida como um submarino, duplicando timer e loot.
        if (Config.Version < 5)
        {
            foreach (var key in Config.Subs.Keys.Where(k => !LootLedger.IsRealSlot(k.Split(':')[^1])).ToList())
                Config.Subs.Remove(key);
            foreach (var key in Config.Voyages.Keys.Where(k => !LootLedger.IsRealSlot(k.Split(':')[^2])).ToList())
                Config.Voyages.Remove(key);
            foreach (var key in Config.SyncedVoyages.Keys.Where(k => !LootLedger.IsRealSlot(k.Split(':')[^2])).ToList())
                Config.SyncedVoyages.Remove(key);
            Config.Version = 5;
        }

        // A versão de testes guardava uma rega só, que vira o plot "Plantação".
        if (Config.Version < 7 && Config.Garden.WateredAt != 0 && !Config.Plots.ContainsKey(GeneralPlot))
            Config.Plots[GeneralPlot] = new PlotRecord { Name = GeneralPlotName, WateredAt = Config.Garden.WateredAt };

        // Quem já usava o plugin continua em português, sem depender do idioma do Dalamud.
        if (Config.Version < 8 && Config.Language.Length == 0)
            Config.Language = "pt";

        // Até a 0.8.2 a posição na chave do plot seguia o idioma do PC (vírgula ou ponto), e um mesmo canteiro
        // tinha chaves diferentes em cada PC. Fica a rega mais recente de cada canteiro.
        if (Config.Version < 9)
        {
            foreach (var (oldKey, plot) in Config.Plots.Where(p => p.Key.Contains(',')).ToList())
            {
                Config.Plots.Remove(oldKey);
                var newKey = oldKey.Replace(',', '.');
                if (!Config.Plots.TryGetValue(newKey, out var other) || other.WateredAt < plot.WateredAt)
                    Config.Plots[newKey] = plot;
            }

            foreach (var plot in Config.Plots.Values.Where(p => IsUnnamed(p.Name)))
                plot.Name = $"Canteiro {plot.Name.Trim()}".Trim();
        }

        Config.Version = 9;
        SaveConfig();
    }

    private void OnCommand(string command, string arguments)
    {
        var argument = arguments.Trim().ToLowerInvariant();
        if (argument is "regar" or "water")
        {
            WaterAll("comando /fct regar");
            ChatGui.Print(L.WateredAll, "FC Timers");
            return;
        }

        mainWindow.Toggle();
    }

    // Rega na mão: vale para todos os plots conhecidos, ou cria o plot geral se ainda não houver nenhum.
    public void WaterAll(string source)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Config.Plots.Count == 0)
            Config.Plots[GeneralPlot] = new PlotRecord { Name = GeneralPlotName };

        foreach (var plot in Config.Plots.Values)
            plot.WateredAt = now;
        SaveConfig();
        Diagnostics.Write($"Rega registrada em todos os plots ({source})");
        QueueLogUpload();
    }

    public void RemovePlot(string key)
    {
        if (Config.Plots.Remove(key))
        {
            SaveConfig();
            Diagnostics.Write($"Plot removido da lista: {key}");
        }
    }

    // O plot regado é o objeto selecionado no momento da mensagem. A chave junta a casa, o quarto e a posição dele,
    // que só muda se alguém mover o plot. Sem um alvo da casa, a rega vai para o plot geral.
    private unsafe void WaterTargetedPlot(string source)
    {
        // Se o alvo já sumiu quando a mensagem chega, vale o último objeto da casa cujo menu foi aberto há pouco.
        // O menu também dá o nome do canteiro ("2nd Patch"), que o alvo não tem.
        var housing = ReadHousingTarget();
        var recent = lastHousingObject is { } last && DateTime.UtcNow - last.SeenAt < TimeSpan.FromMinutes(2) ? last : default((string Key, string Name, bool Indoor, DateTime SeenAt)?);
        if (housing == null && recent is { } fallback)
            housing = (fallback.Key, fallback.Name, fallback.Indoor);
        if (housing is var (targetKey, _, targetIndoor) && recent is { } menu && menu.Key == targetKey)
            housing = (targetKey, menu.Name, targetIndoor);

        string key;
        string name;
        var indoor = false;
        if (housing is var (housingKey, baseName, inside))
        {
            key = housingKey;
            indoor = inside;
            var label = baseName.Trim().Length > 0 ? baseName.Trim() : "Canteiro";
            // O nome do menu ("Canteiro 2") corrige os nomes automáticos; um nome dado na janela fica.
            var fromMenu = AutoName.IsMatch(label);
            name = Config.Plots.TryGetValue(key, out var known) && !IsUnnamed(known.Name) && !(fromMenu && AutoName.IsMatch(known.Name))
                ? known.Name
                : fromMenu ? label : $"{label} {Config.Plots.Values.Count(p => p.Name.StartsWith(label, StringComparison.Ordinal)) + 1}";
        }
        else
        {
            key = GeneralPlot;
            name = GeneralPlotName;
        }

        if (!Config.Plots.TryGetValue(key, out var plot))
        {
            plot = new PlotRecord { Name = name };
            Config.Plots[key] = plot;
        }

        plot.Name = name;
        plot.Indoor = indoor;
        plot.WateredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SaveConfig();
        Diagnostics.Write($"Rega registrada em {plot.Name} ({source}, {(indoor ? "dentro de casa" : "fora")}, chave {key})");
        QueueLogUpload();
    }

    // Chave, nome e se está dentro de casa (sala ou quarto privado), ou null se o alvo não for um objeto da casa.
    private unsafe (string Key, string Name, bool Indoor)? ReadHousingTarget()
    {
        var target = TargetManager.Target;
        var manager = HousingManager.Instance();
        if (target == null || manager == null || target.ObjectKind is not (ObjectKind.HousingEventObject or ObjectKind.EventObj))
            return null;

        // A posição sai sempre com ponto, para a chave ser a mesma em PCs com idiomas diferentes.
        var position = target.Position;
        var coordinates = string.Join(':', new[] { position.X, position.Y, position.Z }.Select(v => v.ToString("F1", CultureInfo.InvariantCulture)));
        var key = $"{ClientState.TerritoryType}:{manager->GetCurrentWard()}:{manager->GetCurrentPlot()}:{manager->GetCurrentRoom()}:{coordinates}";
        return (key, target.Name.TextValue, manager->IsInside());
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
        if (characterEnabled)
        {
            ReadWheels(now);
            ReadActiveBuffs(now);
            ReadSupplies(now);
        }

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

        if (!Config.Characters.TryGetValue(id, out var character))
        {
            character = new CharacterRecord { Name = name };
            Config.Characters[id] = character;
            SaveConfig();
        }
        else if (character.Name != name)
        {
            character.Name = name;
            SaveConfig();
        }

        return character.Enabled;
    }

    private unsafe void ReadSubmarines(long now)
    {
        var manager = HousingManager.Instance();
        var inside = manager != null && manager->WorkshopTerritory != null;
        if (inside != inWorkshop)
        {
            inWorkshop = inside;
            Diagnostics.Write(inside ? "Entrou na oficina" : "Saiu da oficina");
        }

        if (!inside)
            return;

        var workshop = manager->WorkshopTerritory;
        var pointers = workshop->Submersible.DataPointers;
        var changed = false;

        // Rank, peças e desgaste são lidos 1 vez por segundo, e não a cada frame como o loot.
        var conditions = DateTime.UtcNow >= nextScan ? SubmarineParts.ReadCondition() : null;
        var lastVoyages = conditions != null && Config.DetailedLog ? new List<string>() : null;

        // A lista tem 5 posições, mas a FC só tem 4 submarinos: a última repete o sub selecionado.
        for (var slot = 0; slot < LootLedger.MaxSubmarines; slot++)
        {
            var sub = pointers[slot].Value;
            if (sub == null || sub->RankId == 0)
                continue;

            var key = $"{workshop->HouseId}:{slot}";
            var name = sub->NameString;
            long returnTime = sub->ReturnTime;

            changed |= CaptureLoot(slot, key, sub, name, now);
            localReadings.Subs[slot] = now;

            if (!Config.Subs.TryGetValue(key, out var record))
            {
                record = new SubRecord { Name = name, ReturnTime = returnTime };
                Config.Subs[key] = record;
                Diagnostics.Detail($"{name}: visto pela primeira vez, volta {DiagnosticLog.Time(returnTime)}");
                changed = true;
            }
            else if (record.Name != name || record.ReturnTime != returnTime)
            {
                if (record.ReturnTime != returnTime)
                {
                    LogReturnChange(key, name, record.ReturnTime, returnTime, now);
                    record.PreviousReturnTime = record.ReturnTime;
                }

                record.Name = name;
                record.ReturnTime = returnTime;
                changed = true;
            }

            if (conditions != null)
            {
                var condition = conditions.TryGetValue(slot, out var read) ? read : record.Details?.Condition;
                var details = WorkshopScan.Read(sub, DataManager, condition);
                if (details != record.Details)
                {
                    LogDetailsChange(name, record.Details, details);
                    record.Details = details;
                    changed = true;
                }

                lastVoyages?.AddRange(WorkshopScan.DescribeLastVoyage(sub, DataManager));
            }

            Sync.ReportSub(slot, name, returnTime, record.Details);
        }

        if (conditions != null)
        {
            nextScan = DateTime.UtcNow.AddSeconds(1);
            var text = string.Join('\n', lastVoyages ?? []);
            if (lastVoyages != null && text != lastScan)
            {
                lastScan = text;
                if (text.Length > 0)
                    Diagnostics.Detail($"Última viagem na memória:\n{text}");
            }
        }

        if (changed)
            SaveConfig();
    }

    // O loot na memória muda quando o resultado de uma viagem chega. É esse o momento de salvar.
    private unsafe bool CaptureLoot(int slot, string key, HousingWorkshopSubmersibleSubData* sub, string name, long now)
    {
        var signature = SubmarineLoot.Signature(sub);
        if (signature.Length == 0)
            return false;

        var firstSight = !Config.LootSeen.TryGetValue(slot, out var last);
        if (!firstSight && last == signature)
            return false;

        Config.LootSeen[slot] = signature;

        // Na primeira leitura depois de atualizar, o loot na memória pode ser de uma viagem já salva.
        if (firstSight && Config.Voyages.Any(v => LootLedger.SlotOf(v.Key) == slot && SubmarineLoot.Signature(v.Value.Items) == signature))
            return true;

        // O loot é da última viagem que já voltou. Se o sub já saiu de novo, é a volta anterior.
        Config.Subs.TryGetValue(key, out var record);
        var returnTime = new[] { (long)sub->ReturnTime, record?.ReturnTime ?? 0, record?.PreviousReturnTime ?? 0 }
            .FirstOrDefault(t => t != 0 && t <= now);
        if (returnTime == 0)
        {
            Diagnostics.Write($"{name}: loot novo na memória, mas sem horário de volta conhecido. Não foi salvo.");
            QueueLogUpload();
            return true;
        }

        var voyage = SubmarineLoot.Read(sub, name, returnTime, LookupItem, SectorName);
        if (voyage != null && LootLedger.Upsert(Config.Voyages, LootLedger.VoyageKey(key, returnTime), voyage))
        {
            Diagnostics.Write($"{name}: viagem de {DiagnosticLog.Time(returnTime)} salva, {voyage.Items.Count} itens, {LootLedger.Total([voyage]):N0} gil");
            QueueLogUpload();
        }

        return true;
    }

    public string LogText()
        => Diagnostics.Text($"FC Timers {Version} | fuso UTC{DateTimeOffset.Now:zzz} | gerado {DateTime.Now:dd/MM HH:mm}");

    private string CurrentCharacterName()
        => Config.Characters.TryGetValue(CurrentCharacterId, out var character) ? character.Name : "Desconhecido";

    public void SendLog(string source)
    {
        if (!Sync.SendLog(CurrentCharacterName(), LogText()))
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

    private void LogReturnChange(string key, string name, long previous, long current, long now)
    {
        if (current > now && previous != 0 && previous <= now)
        {
            if (Config.Voyages.ContainsKey(LootLedger.VoyageKey(key, previous)))
                Diagnostics.Write($"{name}: saiu de novo, volta {DiagnosticLog.Time(current)}");
            else
                Diagnostics.Write($"{name}: saiu de novo, volta {DiagnosticLog.Time(current)}. O loot da viagem de {DiagnosticLog.Time(previous)} ainda não foi lido.");

            QueueLogUpload();
            return;
        }

        Diagnostics.Detail($"{name}: volta mudou para {DiagnosticLog.Time(current)}");
    }

    private void OnVoyageResult(AddonEvent type, AddonArgs args)
    {
        Diagnostics.Write($"Tela de resultado da viagem aberta ({args.AddonName})");
        if (characterEnabled)
            ReadSubmarines(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    // Registra o menu de objetos da casa, como os plots, e guarda qual objeto foi, para a rega que vier em seguida.
    private unsafe void OnSelectString(AddonEvent type, AddonArgs args)
    {
        var target = TargetManager.Target;
        if (target == null || target.ObjectKind is not (ObjectKind.HousingEventObject or ObjectKind.EventObj))
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        var texts = new List<string>();
        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var value = addon->AtkValues[i];
            if (value.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString)
            {
                var text = value.String.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    texts.Add(text);
            }
        }

        // Por 30 segundos depois do menu de um objeto da casa, toda mensagem do jogo vai para o log,
        // para descobrir qual mensagem a rega de um vaso de dentro de casa mostra.
        gardenWatchUntil = DateTime.UtcNow.AddSeconds(30);

        // Os canteiros de fora não têm nome no alvo; o menu diz qual é ("6th Bed, 2nd Patch").
        if (ReadHousingTarget() is var (key, name, indoor))
        {
            var patch = texts.Select(t => PatchPattern.Match(t)).FirstOrDefault(m => m.Success);
            lastHousingObject = (key, patch != null ? $"Canteiro {patch.Groups[1].Value}" : name, indoor, DateTime.UtcNow);
        }

        Diagnostics.Write($"Menu de {target.Name.TextValue} ({target.ObjectKind}): {string.Join(" | ", texts)}");
    }

    private static readonly Regex AutoName = new(@"^Canteiro \d+$", RegexOptions.Compiled);
    private static readonly Regex PatchPattern = new(@"(\d+)(?:st|nd|rd|th) Patch", RegexOptions.Compiled);

    // Nome vazio ou só um número, das versões que não sabiam o nome dos canteiros de fora.
    private static bool IsUnnamed(string name) => name.Trim().All(char.IsDigit);

    private void OnLogMessage(ILogMessage message)
    {
        var id = message.LogMessageId;
        if (id < FirstGardenMessage || id > LastGardenMessage)
        {
            if (DateTime.UtcNow < gardenWatchUntil)
                {
                    Diagnostics.Write($"Mensagem do jogo {id} depois do menu da casa: {message.GameData.ValueNullable?.Text.ToString()}");
                    QueueLogUpload();
                }
            return;
        }

        Diagnostics.Write($"Mensagem da horta {id}: {message.GameData.ValueNullable?.Text.ToString()}");
        if (characterEnabled && id is CropTendedMessage or SeedsPlantedMessage)
            WaterTargetedPlot($"mensagem do jogo {id}");
    }

    private void OnChatMessage(IChatMessage message)
    {
        if (DateTime.UtcNow < gardenWatchUntil)
            {
                Diagnostics.Write($"Chat {message.LogKind} depois do menu da casa: {message.Message.TextValue}");
                QueueLogUpload();
            }
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

    private static string? SectorName(uint point)
        => DataManager.GetExcelSheet<SubmarineExploration>().GetRowOrDefault(point)?.Destination.ToString();

    private static (string Name, uint Price)? LookupItem(uint itemId)
    {
        var item = DataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        return item == null ? null : (item.Value.Name.ToString(), item.Value.PriceLow);
    }

    // O XP muda a cada leitura de viagem; o resto só quando rank, peças, rota ou desgaste mudam.
    private void LogDetailsChange(string name, SubDetails? previous, SubDetails current)
    {
        var text = $"{name}: rank {current.Rank} ({current.ExpPercent}%), {current.Build}, rota {current.Route} "
            + $"({current.TanksPerVoyage} tanques), peças {current.Condition?.ToString("0.#") ?? "?"}%, reparo {current.RepairKits} kits";
        if (previous == null || previous with { ExpPercent = current.ExpPercent } != current)
        {
            Diagnostics.Write(text);
            QueueLogUpload();
        }
        else
            Diagnostics.Detail(text);
    }

    private void ReadSupplies(long now)
    {
        var record = Config.Supplies;
        var changed = false;
        var emptyPages = new List<int>();
        var chest = Supplies.ReadChest(emptyPages);
        var empty = string.Join(", ", emptyPages);
        if (empty != lastEmptyPages)
        {
            lastEmptyPages = empty;
            if (empty.Length > 0)
                Diagnostics.Write($"Baú da FC: página(s) {empty} carregada(s) sem itens, ignorada(s)");
        }

        foreach (var (page, count) in chest)
        {
            if (record.ChestPages.TryGetValue(page, out var known) && known.Tanks == count.Tanks && known.Kits == count.Kits)
                continue;

            Diagnostics.Write($"Baú da FC, página {page}: {count.Tanks} tanques, {count.Kits} kits de reparo");
            record.ChestPages[page] = new SupplyPage { Tanks = count.Tanks, Kits = count.Kits };
            changed = true;
        }

        if (Supplies.ReadBags() is { } bags && (bags.Tanks != record.Bags.Tanks || bags.Kits != record.Bags.Kits))
        {
            Diagnostics.Write($"Inventário: {bags.Tanks} tanques, {bags.Kits} kits de reparo");
            record.Bags = new SupplyPage { Tanks = bags.Tanks, Kits = bags.Kits };
            changed = true;
        }

        if (!changed)
            return;

        record.ChangedAt = now;
        SaveConfig();
        Diagnostics.Write($"Estoque total: {record.Tanks} tanques, {record.Kits} kits de reparo (páginas do baú vistas: {string.Join(", ", record.ChestPages.Keys.Order())})");
        QueueLogUpload();
    }

    private unsafe void ReadWheels(long now)
    {
        var manager = HousingManager.Instance();
        var slots = WheelReader.Read();
        if (manager == null || slots == null)
            return;

        localReadings.Wheels = now;
        Sync.ReportBuffs(slots.Select(s => new BuffReading(
            s.Slot,
            s.ItemId,
            LookupItem(s.ItemId)?.Name ?? $"Buff {s.Slot + 1}",
            s.Percent >= 100 ? 0 : s.Hours)).ToList());

        var prefix = $"{manager->GetCurrentIndoorHouseId()}:";
        var changed = false;

        foreach (var key in Config.Wheels.Keys.Where(k => k.StartsWith(prefix)).ToList())
        {
            if (slots.All(s => $"{prefix}{s.Slot}" != key))
                changed |= Config.Wheels.Remove(key);
        }

        foreach (var slot in slots)
        {
            var key = $"{prefix}{slot.Slot}";
            var hours = slot.Percent >= 100 ? 0 : slot.Hours;

            // A janela só informa horas inteiras, então o horário só muda quando esse número muda.
            if (Config.Wheels.TryGetValue(key, out var record) && record.ItemId == slot.ItemId && record.Hours == hours)
                continue;

            Config.Wheels[key] = new WheelRecord
            {
                ItemId = slot.ItemId,
                Name = LookupItem(slot.ItemId)?.Name ?? $"Buff {slot.Slot + 1}",
                Hours = hours,
                ReadyTime = now + (hours * 3600L),
            };
            changed = true;
        }

        if (changed)
            SaveConfig();
    }

    private void ReadActiveBuffs(long now)
    {
        var player = ObjectTable.LocalPlayer;

        // Em duty ou trocando de área, os status podem sumir sem o buff ter acabado.
        if (player == null)
            secondsSinceLogin = 0;

        if (player == null || Condition[ConditionFlag.BoundByDuty] || Condition[ConditionFlag.BetweenAreas] || ClientState.IsPvP)
        {
            candidateSeconds = 0;
            return;
        }

        secondsSinceLogin++;

        var active = new List<ActiveStatus>();
        foreach (var status in player.StatusList)
        {
            if (companyStatuses.TryGetValue(status.StatusId, out var name))
                active.Add(new ActiveStatus(name, status.Param));
        }
        active.Sort((a, b) => string.CompareOrdinal(a.BaseName, b.BaseName));

        var signature = string.Join('|', active);
        if (signature != candidateBuffs)
        {
            candidateBuffs = signature;
            candidateSeconds = 0;
        }

        if (++candidateSeconds < ActiveBuffStableSeconds)
            return;

        var matched = ActiveBuffMatcher.Match(active, companyActions, ReadCompanySlots());
        var readings = ActiveBuffMatcher.WithKnownTimes(matched, Config.ActiveFcBuffs, now);

        // Logo depois de logar, um status que aparece é um buff antigo carregando, não um buff novo.
        if (secondsSinceLogin >= ActiveBuffSettleSeconds)
            readings = ActiveBuffMatcher.WithEstimates(readings, active, seenBuffs, ActiveBuffStableSeconds);
        seenBuffs = active.Select(a => a.BaseName).ToHashSet();

        localReadings.ActiveBuffs = now;
        Sync.ReportActiveBuffs(readings);

        var updated = ActiveBuffMatcher.Update(Config.ActiveFcBuffs, readings, now);
        if (updated == null)
            return;

        Diagnostics.Detail($"Buffs ativos da FC: {string.Join(" | ", readings)}");
        Config.ActiveFcBuffs = updated;
        SaveConfig();
    }

    // Cada slot ocupa 3 números a partir da posição 2: ação da FC, ícone e segundos restantes.
    private static unsafe List<CompanySlot> ReadCompanySlots()
    {
        var slots = new List<CompanySlot>();
        var numbers = AtkStage.Instance()->GetNumberArrayData(NumberArrayType.FreeCompanyAction);
        if (numbers == null || numbers->Size < 2)
            return slots;

        var sheet = DataManager.GetExcelSheet<CompanyAction>();
        for (var i = 0; i < numbers->IntArray[1] && i < MaxCompanySlots; i++)
        {
            var first = 2 + (i * 3);
            if (first + 2 >= numbers->Size)
                break;

            var action = sheet.GetRowOrDefault((uint)numbers->IntArray[first]);
            if (action != null)
                slots.Add(new CompanySlot(action.Value.Name.ToString(), numbers->IntArray[first + 2]));
        }

        return slots;
    }

    // Avisa no chat o que ficou pronto, usando também o que os outros membros leram.
    // Só avisa o que já foi visto pendente, então nada antigo avisa ao logar.
    private void CheckAlerts(long now)
    {
        var due = new Dictionary<string, (long Time, string Message)>();
        foreach (var sub in View.Subs.Where(s => s.ReturnTime != 0))
            due[$"sub:{sub.Slot}"] = (sub.ReturnTime, L.SubReturned(sub.Name));
        foreach (var wheel in View.Wheels)
            due[$"wheel:{wheel.Slot}"] = (wheel.ReadyTime, L.WheelReady(wheel.Name));
        foreach (var plot in View.Plots)
            due[$"plot:{plot.Key}"] = (plot.NextWaterAt, L.PlotReady(plot.Name));

        var changed = false;
        foreach (var (id, (time, message)) in due)
        {
            if (time > now)
            {
                if (Config.AlertWatch.GetValueOrDefault(id) != time)
                {
                    Config.AlertWatch[id] = time;
                    changed = true;
                }

                continue;
            }

            if (!Config.AlertWatch.Remove(id, out var watched))
                continue;

            if (watched == time)
                ChatGui.Print(message, "FC Timers");
            changed = true;
        }

        foreach (var id in Config.AlertWatch.Keys.Where(k => !due.ContainsKey(k)).ToList())
            changed |= Config.AlertWatch.Remove(id);

        if (changed)
            SaveConfig();
    }

    private void UpdateDtr(long now)
    {
        var times = View.Subs.Where(s => s.ReturnTime != 0).Select(s => s.ReturnTime)
            .Concat(View.Wheels.Select(w => w.ReadyTime))
            .ToList();
        var ready = times.Count(t => t <= now);
        var pending = times.Where(t => t > now).ToList();

        var parts = new[]
        {
            ready == 0 ? null : L.DtrReady(ready),
            pending.Count == 0 ? null : FormatRemaining(pending.Min() - now),
        };
        var text = string.Join(" | ", parts.Where(p => p != null));

        dtrEntry.Text = $"FC: {(text.Length == 0 ? "--" : text)}";
        dtrEntry.Shown = true;
    }
}
