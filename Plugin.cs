using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
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
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly WindowSystem windowSystem = new("FcTimers");
    private readonly MainWindow mainWindow;
    private readonly IDtrBarEntry dtrEntry;
    private readonly Dictionary<uint, string> companyStatuses;
    private readonly List<CompanyActionInfo> companyActions;
    private DateTime nextUpdate = DateTime.MinValue;
    private string candidateBuffs = string.Empty;
    private int candidateSeconds;
    private int secondsSinceLogin;
    private HashSet<string> seenBuffs = [];

    public Configuration Config { get; }
    public SyncClient Sync { get; }
    public ulong CurrentCharacterId { get; private set; }

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Config.Version < 4)
        {
            Config.Voyages = LootLedger.MigrateKeys(Config.Voyages);
            Config.Version = 4;
            SaveConfig();
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
            SaveConfig();
        }

        Sync = new SyncClient(Config, SaveConfig);

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
        windowSystem.AddWindow(mainWindow);

        dtrEntry = DtrBar.Get("FC Timers");
        dtrEntry.OnClick = _ => mainWindow.Toggle();

        CommandManager.AddHandler(Command, new CommandInfo((_, _) => mainWindow.Toggle())
        {
            HelpMessage = "Abre a janela do FC Timers.",
        });
        CommandManager.AddHandler(ShortCommand, new CommandInfo((_, _) => mainWindow.Toggle())
        {
            HelpMessage = "Atalho para abrir a janela do FC Timers.",
        });

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

    private void OnUpdate(IFramework framework)
    {
        if (DateTime.UtcNow < nextUpdate)
            return;
        nextUpdate = DateTime.UtcNow.AddSeconds(1);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (IsCurrentCharacterEnabled())
        {
            ReadSubmarines(now);
            ReadWheels(now);
            ReadActiveBuffs(now);
        }

        CheckAlerts(now);
        UpdateDtr(now);
        Sync.Tick();
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
        if (manager == null || manager->WorkshopTerritory == null)
            return;

        var workshop = manager->WorkshopTerritory;
        var pointers = workshop->Submersible.DataPointers;
        var changed = false;

        // A lista tem 5 posições, mas a FC só tem 4 submarinos: a última repete um deles.
        for (var slot = 0; slot < LootLedger.MaxSubmarines; slot++)
        {
            var sub = pointers[slot].Value;
            if (sub == null || sub->RankId == 0)
                continue;

            var key = $"{workshop->HouseId}:{slot}";
            var name = sub->NameString;
            long returnTime = sub->ReturnTime;

            Sync.ReportSub(slot, name, returnTime);

            var voyage = SubmarineLoot.Read(sub, name, now, LookupItem);
            if (voyage != null)
                changed |= LootLedger.Upsert(Config.Voyages, LootLedger.VoyageKey(key, voyage.ReturnTime), voyage);

            if (Config.Subs.TryGetValue(key, out var record))
            {
                if (record.Name == name && record.ReturnTime == returnTime)
                    continue;

                record.Name = name;
                if (record.ReturnTime != returnTime)
                    record.Notified = false;
                record.ReturnTime = returnTime;
            }
            else
            {
                // Na primeira leitura, o que já voltou não gera alerta.
                Config.Subs[key] = new SubRecord { Name = name, ReturnTime = returnTime, Notified = returnTime <= now };
            }

            changed = true;
        }

        if (changed)
            SaveConfig();
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
            var ready = slot.Percent >= 100;
            var hours = ready ? 0 : slot.Hours;

            // A janela só informa horas inteiras, então o horário só muda quando esse número muda.
            if (Config.Wheels.TryGetValue(key, out var record) && record.ItemId == slot.ItemId && record.Hours == hours)
                continue;

            var name = LookupItem(slot.ItemId)?.Name ?? $"Buff {slot.Slot + 1}";

            // Na primeira leitura, o que já está pronto não gera alerta.
            var notified = record == null
                ? hours == 0
                : record.ItemId == slot.ItemId && record.Notified && hours <= record.Hours;
            Config.Wheels[key] = new WheelRecord
            {
                ItemId = slot.ItemId,
                Name = name,
                Hours = hours,
                ReadyTime = now + (hours * 3600L),
                Notified = notified,
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

        Sync.ReportActiveBuffs(readings);

        var updated = ActiveBuffMatcher.Update(Config.ActiveFcBuffs, readings, now);
        if (updated == null)
            return;

        Log.Information($"Buffs ativos da FC: {string.Join(" | ", readings)}");
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

    private void CheckAlerts(long now)
    {
        var changed = false;

        foreach (var sub in Config.Subs.Values.Where(s => s.ReturnTime != 0 && !s.Notified && s.ReturnTime <= now))
        {
            ChatGui.Print($"Submarino {sub.Name} voltou!", "FC Timers");
            sub.Notified = true;
            changed = true;
        }

        foreach (var wheel in Config.Wheels.Values.Where(w => !w.Notified && w.ReadyTime <= now))
        {
            ChatGui.Print($"{wheel.Name} está pronta!", "FC Timers");
            wheel.Notified = true;
            changed = true;
        }

        if (changed)
            SaveConfig();
    }

    private void UpdateDtr(long now)
    {
        var times = Config.Subs.Values.Where(s => s.ReturnTime != 0).Select(s => s.ReturnTime)
            .Concat(Config.Wheels.Values.Select(w => w.ReadyTime))
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
