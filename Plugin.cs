using System;
using System.Collections.Generic;
using System.Linq;
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
    private DateTime autoLogAt = DateTime.MaxValue;
    private DateTime lastAutoLog = DateTime.MinValue;

    public Configuration Config { get; }
    public SyncClient Sync { get; }
    public DiagnosticLog Diagnostics { get; }
    public FcView View { get; private set; } = new();
    public ulong CurrentCharacterId { get; private set; }

    public static string Version => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "?";

    public Plugin()
    {
        var saved = PluginInterface.GetPluginConfig() as Configuration;
        Config = saved ?? new Configuration { LastChangelogVersion = Version };
        Migrate();

        Diagnostics = new DiagnosticLog(
            System.IO.Path.Combine(PluginInterface.GetPluginConfigDirectory(), "diagnostico.txt"),
            message => Log.Information(message),
            () => Config.DetailedLog);
        Diagnostics.Write($"Plugin {Version} iniciado");

        Sync = new SyncClient(Config, SaveConfig, Diagnostics.Write);

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
            HelpMessage = "Abre a janela do FC Timers. Use \"/fct regar\" depois de regar a plantação.",
        });
        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Atalho do /fctimers. \"/fct regar\" registra a rega da plantação.",
        });

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, ResultAddons, OnVoyageResult);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnSelectString);
        ChatGui.LogMessage += OnLogMessage;

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
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", OnSelectString);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, ResultAddons, OnVoyageResult);
        CommandManager.RemoveHandler(ShortCommand);
        CommandManager.RemoveHandler(Command);
        dtrEntry.Remove();
        windowSystem.RemoveAllWindows();
        Sync.Dispose();
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(Config);

    public static string FormatRemaining(long seconds)
    {
        if (seconds <= 0)
            return "pronto";

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

        Config.Version = 7;
        SaveConfig();
    }

    private void OnCommand(string command, string arguments)
    {
        var argument = arguments.Trim().ToLowerInvariant();
        if (argument is "regar" or "water")
        {
            WaterAll("comando /fct regar");
            ChatGui.Print("Rega registrada em todos os plots.", "FC Timers");
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
        var target = TargetManager.Target;
        var manager = HousingManager.Instance();
        string key;
        string name;
        if (target != null && manager != null && target.ObjectKind is ObjectKind.HousingEventObject or ObjectKind.EventObj)
        {
            var position = target.Position;
            key = $"{ClientState.TerritoryType}:{manager->GetCurrentWard()}:{manager->GetCurrentPlot()}:{manager->GetCurrentRoom()}:{position.X:F1}:{position.Y:F1}:{position.Z:F1}";
            var baseName = target.Name.TextValue;
            name = Config.Plots.TryGetValue(key, out var known)
                ? known.Name
                : $"{baseName} {Config.Plots.Values.Count(p => p.Name.StartsWith(baseName, StringComparison.Ordinal)) + 1}";
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

        plot.WateredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SaveConfig();
        Diagnostics.Write($"Rega registrada em {plot.Name} ({source}, chave {key})");
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
        }

        foreach (var (key, plot) in Config.Plots.Where(p => p.Value.WateredAt != 0))
        {
            Sync.ReportPlot(
                key,
                plot.Name,
                plot.WateredAt,
                plot.NextWaterAt,
                plot.DieAt);
        }

        if (DateTime.UtcNow >= autoLogAt)
        {
            autoLogAt = DateTime.MaxValue;
            lastAutoLog = DateTime.UtcNow;
            SendLog("automático, loot não lido");
        }

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
            Sync.ReportSub(slot, name, returnTime);

            if (Config.Subs.TryGetValue(key, out var record))
            {
                if (record.Name == name && record.ReturnTime == returnTime)
                    continue;

                if (record.ReturnTime != returnTime)
                {
                    LogReturnChange(key, name, record.ReturnTime, returnTime, now);
                    record.PreviousReturnTime = record.ReturnTime;
                }

                record.Name = name;
                record.ReturnTime = returnTime;
            }
            else
            {
                Config.Subs[key] = new SubRecord { Name = name, ReturnTime = returnTime };
                Diagnostics.Detail($"{name}: visto pela primeira vez, volta {DiagnosticLog.Time(returnTime)}");
            }

            changed = true;
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

        var voyage = SubmarineLoot.Read(sub, name, returnTime, LookupItem);
        if (voyage != null && LootLedger.Upsert(Config.Voyages, LootLedger.VoyageKey(key, returnTime), voyage))
            Diagnostics.Write($"{name}: viagem de {DiagnosticLog.Time(returnTime)} salva, {voyage.Items.Count} itens, {LootLedger.Total([voyage]):N0} gil");

        return true;
    }

    public string LogText()
        => Diagnostics.Text($"FC Timers {Version} | fuso UTC{DateTimeOffset.Now:zzz} | gerado {DateTime.Now:dd/MM HH:mm}");

    public void SendLog(string source)
    {
        var name = Config.Characters.TryGetValue(CurrentCharacterId, out var character) ? character.Name : "Desconhecido";
        if (Sync.SendLog(name, LogText()))
            Diagnostics.Write($"Enviando o log ao serviço ({source})");
    }

    // Quando um loot se perde, o log vai sozinho ao serviço, uns segundos depois para incluir o que vier em seguida.
    // No máximo um envio automático a cada 10 minutos.
    private void QueueLogUpload()
    {
        if (autoLogAt == DateTime.MaxValue && DateTime.UtcNow - lastAutoLog > TimeSpan.FromMinutes(10))
            autoLogAt = DateTime.UtcNow.AddSeconds(30);
    }

    private void LogReturnChange(string key, string name, long previous, long current, long now)
    {
        if (current > now && previous != 0 && previous <= now)
        {
            if (Config.Voyages.ContainsKey(LootLedger.VoyageKey(key, previous)))
                Diagnostics.Detail($"{name}: saiu de novo, volta {DiagnosticLog.Time(current)}");
            else
            {
                Diagnostics.Write($"{name}: saiu de novo, volta {DiagnosticLog.Time(current)}. O loot da viagem de {DiagnosticLog.Time(previous)} ainda não foi lido.");
                QueueLogUpload();
            }

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

    // Registra o menu de objetos da casa, como os plots, para confirmar como a horta funciona.
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

        Diagnostics.Write($"Menu de {target.Name.TextValue} ({target.ObjectKind}): {string.Join(" | ", texts)}");
    }

    private void OnLogMessage(ILogMessage message)
    {
        var id = message.LogMessageId;
        if (id < FirstGardenMessage || id > LastGardenMessage)
            return;

        Diagnostics.Write($"Mensagem da horta {id}: {message.GameData.ValueNullable?.Text.ToString()}");
        if (characterEnabled && id is CropTendedMessage or SeedsPlantedMessage)
            WaterTargetedPlot($"mensagem do jogo {id}");
    }

    private static (string Name, uint Price)? LookupItem(uint itemId)
    {
        var item = DataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        return item == null ? null : (item.Value.Name.ToString(), item.Value.PriceLow);
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
            due[$"sub:{sub.Slot}"] = (sub.ReturnTime, $"Submarino {sub.Name} voltou!");
        foreach (var wheel in View.Wheels)
            due[$"wheel:{wheel.Slot}"] = (wheel.ReadyTime, $"{wheel.Name} está pronta!");
        foreach (var plot in View.Plots)
            due[$"plot:{plot.Key}"] = (plot.NextWaterAt, $"{plot.Name} já pode ser regado!");

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
            ready == 0 ? null : ready == 1 ? "1 pronto" : $"{ready} prontos",
            pending.Count == 0 ? null : FormatRemaining(pending.Min() - now),
        };
        var text = string.Join(" | ", parts.Where(p => p != null));

        dtrEntry.Text = $"FC: {(text.Length == 0 ? "--" : text)}";
        dtrEntry.Shown = true;
    }
}
