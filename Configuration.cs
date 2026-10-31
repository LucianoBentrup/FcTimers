using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace FcTimers;

[Serializable]
public class SubRecord
{
    public string Name { get; set; } = string.Empty;
    public long ReturnTime { get; set; }
    public bool Notified { get; set; }
}

[Serializable]
public class WheelRecord
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Hours { get; set; }
    public long ReadyTime { get; set; }
    public bool Notified { get; set; }
}

[Serializable]
public class CharacterRecord
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    // Chave: ID interno do personagem.
    public Dictionary<ulong, CharacterRecord> Characters { get; set; } = [];

    public bool SyncEnabled { get; set; }
    public string ServiceUrl { get; set; } = string.Empty;
    public string ServiceKey { get; set; } = string.Empty;

    public List<ActiveBuffRecord> ActiveFcBuffs { get; set; } = [];

    // Viagens já entregues ao serviço. Chave: a mesma de Voyages.
    public Dictionary<string, string> SyncedVoyages { get; set; } = [];

    // Chave: "<id da casa>:<slot>:<hora de retorno>"
    public Dictionary<string, VoyageRecord> Voyages { get; set; } = [];

    // Chave: "<id da casa>:<slot>"
    public Dictionary<string, SubRecord> Subs { get; set; } = [];

    // Chave: "<id da casa>:<slot>"
    public Dictionary<string, WheelRecord> Wheels { get; set; } = [];
}
