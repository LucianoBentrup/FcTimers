using System;

namespace FcTimers;

// O dia do loot vira à meia-noite de Brasília, igual ao painel, em qualquer PC.
public static class GameTime
{
    public static readonly TimeZoneInfo PanelZone = FindPanelZone();

    private static TimeZoneInfo FindPanelZone()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Brasilia", TimeSpan.FromHours(-3), "Brasília", "Brasília");
    }

    public static DateTime Today => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, PanelZone).Date;
}
