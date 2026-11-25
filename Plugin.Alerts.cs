using System;
using System.Collections.Generic;
using System.Linq;

namespace FcTimers;

public sealed partial class Plugin
{
    // Avisa no chat o que ficou pronto, usando também o que os outros membros leram.
    // Só avisa o que já foi visto pendente, então nada antigo avisa ao logar.
    private void CheckAlerts(long now)
    {
        var due = new Dictionary<string, (long Time, string Message)>();
        if (Config.AlertSubs)
        {
            foreach (var sub in View.Subs.Where(s => s.ReturnTime != 0))
                due[$"sub:{sub.Slot}"] = (sub.ReturnTime, L.SubReturned(sub.Name));
        }

        if (Config.AlertAirships)
        {
            foreach (var ship in View.Airships.Where(s => s.ReturnTime != 0))
                due[$"air:{ship.Slot}"] = (ship.ReturnTime, L.AirshipReturned(ship.Name));
        }

        // Casa da FC: um aviso em cada marco (30, 40 e 44 dias). Uma visita nova muda o horário e recomeça.
        if (Config.AlertHouse && View.House is { } house)
        {
            foreach (var days in HouseWarningDays)
                due[$"house:{days}"] = (house.VisitedAt + (days * 86400), L.HouseWarning(days));
        }

        if (Config.AlertWheels)
        {
            foreach (var wheel in View.Wheels)
                due[$"wheel:{wheel.Slot}"] = (wheel.ReadyTime, L.WheelReady(wheel.Name));
        }

        if (Config.AlertPlots)
        {
            foreach (var plot in View.Plots.Where(p => p.NextWaterAt != 0))
                due[$"plot:{plot.Key}"] = (plot.NextWaterAt, L.PlotReady(plot.Name));
        }

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
                Notify(message);
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
