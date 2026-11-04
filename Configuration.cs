using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;

namespace FcTimers;

[Serializable]
public class SubRecord
{
    public string Name { get; set; } = string.Empty;
    public long ReturnTime { get; set; }

    // Volta anterior, para o loot que só chega depois de o sub já ter saído de novo.
    public long PreviousReturnTime { get; set; }

    public SubDetails? Details { get; set; }
}

// Rank, peças, rota e desgaste, lidos quando a lista de subs da oficina é aberta.
[Serializable]
public sealed record SubDetails
{
    public int Rank { get; set; }
    public int ExpPercent { get; set; }

    // Letra da classe de cada peça (casco, popa, proa, ponte), com "+" nas modificadas. Ex.: "S+S+U+C+".
    public string Build { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int TanksPerVoyage { get; set; }

    // Kits que um reparo completo das 4 peças gasta.
    public int RepairKits { get; set; }

    // Desgaste da peça mais gasta, de 0 a 100. Null enquanto não foi lido.
    public double? Condition { get; set; }
}

[Serializable]
public class WheelRecord
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Hours { get; set; }
    public long ReadyTime { get; set; }
}

[Serializable]
public class CharacterRecord
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

[Serializable]
public class SupplyPage
{
    public int Tanks { get; set; }
    public int Kits { get; set; }
}

[Serializable]
public class SupplyRecord
{
    // Última contagem de cada página do baú da FC (1 a 5) e do inventário de quem joga neste PC.
    public Dictionary<int, SupplyPage> ChestPages { get; set; } = [];
    public SupplyPage Bags { get; set; } = new();

    // Quando o total mudou pela última vez. Uma contagem antiga nunca passa por cima de uma mais nova de outro membro.
    public long ChangedAt { get; set; }

    public int Tanks => ChestPages.Values.Sum(p => p.Tanks) + Bags.Tanks;
    public int Kits => ChestPages.Values.Sum(p => p.Kits) + Bags.Kits;
}

[Serializable]
public class GardenRecord
{
    // Horário da última rega registrada neste PC.
    public long WateredAt { get; set; }
}

[Serializable]
public class PlotRecord
{
    // Pela wiki, a planta precisa de rega pelo menos 1 vez por dia. Sem rega, as que murcham mais rápido
    // ficam roxas em 24 horas e morrem 24 horas depois. O plugin não sabe a semente, então usa o caso mais curto.
    public const long WaterIntervalSeconds = 24 * 3600;
    public const long DieAfterSeconds = 48 * 3600;

    public string Name { get; set; } = string.Empty;

    // Horário da última rega deste plot registrada neste PC.
    public long WateredAt { get; set; }

    // Vaso dentro de casa ou de quarto privado. Só esses aparecem pelo nome nos avisos do Discord.
    public bool Indoor { get; set; }

    public long NextWaterAt => WateredAt + WaterIntervalSeconds;
    public long DieAt => WateredAt + DieAfterSeconds;
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 9;

    // "pt" ou "en". Vazio: segue o idioma do Dalamud.
    public string Language { get; set; } = string.Empty;

    // Mundo ou data center dos preços do market. Vazio: o mundo de origem do último personagem marcado.
    public string MarketWorld { get; set; } = string.Empty;
    public string HomeWorld { get; set; } = string.Empty;
    public uint HomeDataCenter { get; set; }

    // Versão cujo changelog já foi mostrado.
    public string LastChangelogVersion { get; set; } = string.Empty;

    // Chave: ID interno do personagem.
    public Dictionary<ulong, CharacterRecord> Characters { get; set; } = [];

    public bool SyncEnabled { get; set; }
    public string ServiceUrl { get; set; } = string.Empty;
    public string ServiceKey { get; set; } = string.Empty;

    // Log de diagnóstico com cada leitura dos subs e os envios, além dos eventos de loot.
    public bool DetailedLog { get; set; }

    public List<ActiveBuffRecord> ActiveFcBuffs { get; set; } = [];

    // Viagens já entregues ao serviço. Chave: a mesma de Voyages.
    public Dictionary<string, string> SyncedVoyages { get; set; } = [];

    // Chave: "<id da casa>:<slot>:<hora de retorno>"
    public Dictionary<string, VoyageRecord> Voyages { get; set; } = [];

    // Chave: "<id da casa>:<slot>"
    public Dictionary<string, SubRecord> Subs { get; set; } = [];

    // Chave: "<id da casa>:<slot>"
    public Dictionary<string, WheelRecord> Wheels { get; set; } = [];

    // Último loot visto na memória de cada slot. Loot novo só é salvo quando muda.
    public Dictionary<int, string> LootSeen { get; set; } = [];

    // Avisos no chat que ainda vão sair. Chave: o que avisa; valor: quando.
    public Dictionary<string, long> AlertWatch { get; set; } = [];

    // Só para migrar a rega geral da versão de testes. Hoje cada plot tem a sua.
    public GardenRecord Garden { get; set; } = new();

    // Chave: território, ward, plot da casa e posição do plot ou vaso. "geral" quando não dá para saber qual foi.
    public Dictionary<string, PlotRecord> Plots { get; set; } = [];

    public SupplyRecord Supplies { get; set; } = new();
}
