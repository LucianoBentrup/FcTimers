using System;

namespace FcTimers;

// Decide se um personagem é da FC do painel. O ID da FC, que o jogo dá, vale mais que a tag:
// a tag pode ser igual em FCs de mundos diferentes e muda quando a FC troca de tag.
public static class FcIdentity
{
    // true: outra FC. false: a FC do painel. null: ainda não dá para saber.
    public static bool? IsOtherFc(CharacterRecord character, Configuration config)
    {
        if (config.FcId != 0 && character.FcId != 0)
            return character.FcId != config.FcId;

        if (character.FcTag.Length > 0 && config.FcTag.Length > 0 && character.FcTag != config.FcTag)
            return true;

        return null;
    }

    // ID aleatório desta instalação, que representa o dono no serviço. Não é o ContentId, que não sai do PC.
    public static string NewOwnerId() => Guid.NewGuid().ToString("N");
}
