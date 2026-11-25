using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FcTimers;

public sealed partial class Plugin
{
    // Casa da FC: mundo, ward e plot da casa onde fica a oficina. Fica guardada para reconhecer as próximas entradas.
    private static string? HouseKey(HouseId id)
        => id.Id == 0 || id.IsApartment ? null : $"{id.WorldId}:{id.WardIndex}:{id.PlotIndex}";

    private bool LearnFcHouse(HouseId id)
    {
        // Só a primeira oficina ensina a casa. Uma oficina de outra casa é de outra FC e não troca a casa da FC.
        if (Config.FcHouse.Length > 0 || HouseKey(id) is not { } house)
            return false;

        Config.FcHouse = house;
        Diagnostics.Write($"Casa da FC reconhecida pela oficina ({house})");
        return true;
    }

    // Oficina de outra FC (de um alt de sub, por exemplo): os subs, o loot e as airships dela não são lidos nem enviados,
    // para não irem para o painel da FC no lugar dos subs de verdade. Entrar na oficina da FC libera o personagem de novo.
    private bool CheckWorkshopFc(HouseId id)
    {
        if (HouseKey(id) is not { } house)
            return true;

        var changed = LearnFcHouse(id);
        var other = house != Config.FcHouse;
        if (Config.Characters.TryGetValue(CurrentCharacterId, out var character))
        {
            if (character.OtherFc != other)
            {
                character.OtherFc = other;
                changed = true;
                Diagnostics.Write(other
                    ? $"Oficina de outra FC ({house}; casa da FC: {Config.FcHouse}). Nada deste personagem é lido nem enviado. Se a FC mudou de casa, use o botão \"Esta é a casa da FC\" dentro da casa nova."
                    : "Oficina da FC: este personagem volta a ler e enviar dados");
            }

            if (!other && character.FcTag.Length > 0 && Config.FcTag != character.FcTag)
            {
                Config.FcTag = character.FcTag;
                changed = true;
            }

            currentOtherFc = character.OtherFc;
        }

        if (changed)
            SaveConfig();
        return !other;
    }

    // Casa em que o personagem ativo está agora (fora de apartamento). Null fora de casa.
    public string? CurrentHouse => lastIndoorHouse;

    // Botão do card da casa: marca a casa onde o personagem está como a casa da FC.
    public void MarkCurrentHouse()
    {
        if (lastIndoorHouse is not { } house || house == Config.FcHouse)
            return;

        Config.FcHouse = house;
        Config.FcHouseManual = true;

        // Quem marca a casa é da FC dela: o personagem volta a valer e a tag dele vira a da FC.
        if (Config.Characters.TryGetValue(CurrentCharacterId, out var character))
        {
            character.OtherFc = false;
            currentOtherFc = false;
            if (character.FcTag.Length > 0)
            {
                Config.FcTag = character.FcTag;
                foreach (var same in Config.Characters.Values.Where(c => c.FcTag == character.FcTag))
                    same.OtherFc = false;
            }
        }

        SaveConfig();
        Diagnostics.Write($"Casa da FC marcada à mão ({house})");
    }

    // Entrar na casa da FC zera o prazo de 45 dias do jogo. O registro vale de 10 em 10 minutos, para não gravar a cada segundo.
    private unsafe void CheckHouseVisit(long now)
    {
        var manager = HousingManager.Instance();
        if (manager == null || !(manager->IsInside() || manager->IsInWorkshop()))
        {
            lastIndoorHouse = null;
            return;
        }

        // Toda casa em que o personagem entra vai ao log, para conferir no jogo se a casa da FC é reconhecida.
        var current = HouseKey(manager->GetCurrentIndoorHouseId());
        if (current != lastIndoorHouse)
        {
            lastIndoorHouse = current;
            Diagnostics.Detail($"Dentro da casa {current ?? "?"} (casa da FC: {(Config.FcHouse.Length == 0 ? "?" : Config.FcHouse)})");
        }

        if (currentOtherFc || Config.FcHouse.Length == 0 || current != Config.FcHouse || now - Config.HouseVisitAt < HouseVisitStep)
            return;

        if (now - Config.HouseVisitAt >= HouseVisitLogGap)
            Diagnostics.Write("Entrou na casa da FC");
        Config.HouseVisitAt = now;
        SaveConfig();
        Sync.ReportHouseVisit(now, CurrentCharacterName());
    }
}
