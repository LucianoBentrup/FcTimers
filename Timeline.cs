using System.Collections.Generic;
using System.Linq;

namespace FcTimers;

public enum TimelineLane
{
    Subs,
    Airships,
    Garden,
    Buffs,
    House,
}

public enum TimelineKind
{
    Returns,
    WaterFrom,
    PlotDies,
    WheelReady,
    BuffEnds,
    HouseDemolish,
}

// Um marco da linha do tempo. Ready: já passou da hora e espera alguém (sub de volta, planta para regar).
public readonly record struct TimelineMarker(TimelineLane Lane, TimelineKind Kind, long Time, string Name, bool Ready);

// Os marcos das próximas horas da FC, tirados da mesma visão que o resto da janela usa.
// Os marcos depois da janela também voltam: a linha do tempo mostra o próximo deles na ponta da faixa.
public static class Timeline
{
    public const long WindowSeconds = 24 * 3600;

    public static List<TimelineMarker> Build(FcView view, long now, long houseLimitSeconds)
    {
        var markers = new List<TimelineMarker>();

        foreach (var sub in view.Subs.Where(s => s.ReturnTime > 0))
            markers.Add(new(TimelineLane.Subs, TimelineKind.Returns, sub.ReturnTime, sub.Name, sub.ReturnTime <= now));

        foreach (var ship in view.Airships.Where(s => s.ReturnTime > 0))
            markers.Add(new(TimelineLane.Airships, TimelineKind.Returns, ship.ReturnTime, ship.Name, ship.ReturnTime <= now));

        foreach (var plot in view.Plots.Where(p => p.NextWaterAt > 0))
        {
            if (plot.NextWaterAt > now)
            {
                markers.Add(new(TimelineLane.Garden, TimelineKind.WaterFrom, plot.NextWaterAt, plot.Name, false));
            }
            else if (plot.DieAt > now)
            {
                // Já pode regar: fica pronto agora, e o prazo para não morrer vira outro marco.
                markers.Add(new(TimelineLane.Garden, TimelineKind.WaterFrom, plot.NextWaterAt, plot.Name, true));
                markers.Add(new(TimelineLane.Garden, TimelineKind.PlotDies, plot.DieAt, plot.Name, false));
            }
        }

        foreach (var wheel in view.Wheels.Where(w => w.ReadyTime > 0))
            markers.Add(new(TimelineLane.Buffs, TimelineKind.WheelReady, wheel.ReadyTime, wheel.Name, wheel.ReadyTime <= now));

        foreach (var buff in view.ActiveBuffs.Where(b => b.EndTime > now))
            markers.Add(new(TimelineLane.Buffs, TimelineKind.BuffEnds, buff.EndTime, buff.Name, false));

        if (view.House is { } house)
        {
            var demolish = house.VisitedAt + houseLimitSeconds;
            markers.Add(new(TimelineLane.House, TimelineKind.HouseDemolish, demolish, string.Empty, false));
        }

        return markers.OrderBy(m => m.Time).ToList();
    }

    // Faixas que aparecem: só as que têm algum marco, na ordem do enum.
    public static List<TimelineLane> Lanes(IEnumerable<TimelineMarker> markers)
        => markers.Select(m => m.Lane).Distinct().OrderBy(l => l).ToList();

    // O próximo marco que ainda vai acontecer, para a frase em cima da linha ("Próximo: Gaivota volta em 1h20").
    public static TimelineMarker? Next(IEnumerable<TimelineMarker> markers, long now)
        => markers.Where(m => !m.Ready && m.Time > now).OrderBy(m => m.Time).Cast<TimelineMarker?>().FirstOrDefault();
}
