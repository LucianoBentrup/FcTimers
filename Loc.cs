namespace FcTimers;

// Textos do plugin em português e inglês. O log de diagnóstico e o painel do Discord continuam em português.
public static class L
{
    public static bool English { get; set; }

    private static string T(string pt, string en) => English ? en : pt;

    // Abas
    public static string TabSubs => T("Submarinos", "Submarines");
    public static string TabLoot => T("Loot", "Loot");
    public static string TabBuffs => T("Buffs", "Buffs");
    public static string TabGarden => T("Plantação", "Garden");
    public static string TabSettings => T("Configurações", "Settings");

    // Submarinos
    public static string NoSubs => T("Nenhum dado ainda. Abra a lista de submarinos na oficina da FC.", "No data yet. Open the submarine list in the FC workshop.");
    public static string Idle => T("parado", "idle");
    public static string Ready => T("pronto", "ready");
    public static string Wear(string percent) => T($"peças {percent}", $"parts {percent}");
    public static string NeedsRepair => T("precisa de reparo", "needs repair");
    public static string SetupLine(string build, string route, int tanks) => T($"{build} · {route} · {tanks} tanques por viagem", $"{build} · {route} · {tanks} tanks per voyage");
    public static string Supplies => T("Estoque", "Supplies");
    public static string NoSupplies => T("Nenhum dado ainda. Abra a aba do baú da FC onde ficam os tanques.", "No data yet. Open the FC chest tab where the tanks are kept.");
    public static string Tanks(string count) => T($"Tanques de ceruleum: {count}", $"Ceruleum tanks: {count}");
    public static string Kits(string count) => T($"Kits de reparo: {count}", $"Repair materials: {count}");
    public static string Rounds(int rounds) => rounds == 1 ? T("≈ 1 rodada", "≈ 1 round") : T($"≈ {rounds} rodadas", $"≈ {rounds} rounds");
    public static string Repairs(int repairs) => repairs == 1 ? T("≈ 1 reparo completo", "≈ 1 full repair") : T($"≈ {repairs} reparos completos", $"≈ {repairs} full repairs");
    public static string CountedNow => T("Contado agora, no baú da FC e no inventário.", "Counted just now, from the FC chest and inventory.");
    public static string CountedAgo(string time) => T($"Contado há {time}, no baú da FC e no inventário.", $"Counted {time} ago, from the FC chest and inventory.");

    // Loot
    public static string LootTitle => T("Loot por dia (valor de venda para NPC, dia de Brasília)", "Loot per day (NPC sell value, Brasília day)");
    public static string NoVoyages => T("Nenhuma viagem registrada ainda.", "No voyages recorded yet.");
    public static string Voyages(int count) => count == 1 ? T("1 viagem", "1 voyage") : T($"{count} viagens", $"{count} voyages");
    public static string DayTotal(string total, string voyages) => T($"{total} gil em {voyages}", $"{total} gil in {voyages}");
    public static string IncludesMarket(string value) => T($"inclui {value} gil de market", $"includes {value} gil from the market board");
    public static string MarketTag => T("market", "market");
    public static string Market => T("Preços do market", "Market prices");
    public static string MarketAuto(string world) => T($"Automático ({world})", $"Automatic ({world})");
    public static string MarketHint => T("Itens que valem mais no market contam pelo preço do market (média das vendas recentes no Universalis).", "Items worth more on the market board count at the market price (average of recent sales on Universalis).");

    // Buffs
    public static string Wheels => T("Mesa da roda", "Wheel stand");
    public static string NoWheels => T("Nenhum buff registrado. Abra a mesa da roda na casa da FC.", "No wheels recorded. Open the wheel stand in the FC house.");
    public static string ActiveBuffs => T("Buffs ativos", "Active buffs");
    public static string None => T("Nenhum", "None");

    // Plantação
    public static string NoPlots => T("Nenhuma rega registrada. Regue um plot com o plugin ligado.", "No watering recorded. Water a plot with the plugin enabled.");
    public static string Remove => T("Remover", "Remove");
    public static string WaterAll => T("Reguei todos agora", "Watered all now");
    public static string WaterAllHint => T("ou /fct regar. Cada plot também é registrado sozinho ao regar.", "or /fct water. Each plot is also picked up on its own when watered.");
    public static string WaterIn(string time) => T($"regar em {time}", $"water in {time}");
    public static string WaterNow(string time) => T($"pronta para regar. Morre em {time}", $"ready to water. Dies in {time}");
    public static string WaterLate => T("passou do prazo de rega", "past the watering deadline");
    public static string WaterReady => T("pronta para regar", "ready to water");

    // Configurações
    public static string Language => T("Idioma", "Language");
    public static string Characters => T("Personagens que leem e enviam dados", "Characters that read and send data");
    public static string Current => T("atual", "current");
    public static string SyncEnabled => T("Enviar e receber dados do painel da FC", "Send and receive FC panel data");
    public static string ServiceUrl => T("Endereço do serviço", "Service address");
    public static string ServiceKey => T("Chave da FC", "FC key");
    public static string Sent(string status) => T($"Envio: {status}", $"Sending: {status}");
    public static string Received(string time) => T($"Recebido: {time}", $"Received: {time}");
    public static string NothingReceived => T("Recebido: ainda nada, mostrando só os dados deste PC", "Received: nothing yet, showing only this PC's data");
    public static string Diagnostics => T("Diagnóstico", "Diagnostics");
    public static string DetailedLog => T("Log detalhado", "Detailed log");
    public static string DetailedLogHint => T("Desligado: só loot, rega, estoque e erros. Ligado: também cada leitura e envio.", "Off: only loot, watering, supplies and errors. On: every reading and upload too.");
    public static string CopyLog => T("Copiar log", "Copy log");
    public static string SendLog => T("Enviar log", "Send log");
    public static string Copied => T("Copiado. Cole no Discord.", "Copied. Paste it on Discord.");
    public static string LogHasNoKey => T("O log não inclui a chave da FC.", "The log never includes the FC key.");
    public static string SendLogHint => T("Enviar log guarda o log no serviço da FC. Ele também vai sozinho a cada 30 minutos, quando tem novidade, e logo depois de um loot se perder.", "Send log stores the log on the FC service. It is also sent automatically every 30 minutes when there is something new, and right after loot is missed.");

    // Chat e barra do jogo
    public static string SubReturned(string name) => T($"Submarino {name} voltou!", $"Submarine {name} is back!");
    public static string WheelReady(string name) => T($"{name} está pronta!", $"{name} is ready!");
    public static string PlotReady(string name) => T($"{name} já pode ser regado!", $"{name} can be watered now!");
    public static string WateredAll => T("Rega registrada em todos os plots.", "Watering recorded on every plot.");
    public static string DtrReady(int count) => count == 1 ? T("1 pronto", "1 ready") : T($"{count} prontos", $"{count} ready");
    public static string HelpMain => T("Abre a janela do FC Timers. Use \"/fct regar\" depois de regar a plantação.", "Opens the FC Timers window. Use \"/fct water\" after watering the garden.");
    public static string HelpShort => T("Atalho do /fctimers. \"/fct regar\" registra a rega da plantação.", "Shortcut for /fctimers. \"/fct water\" records the garden watering.");
}
