namespace FcTimers;

// Textos do plugin em português e inglês. O log de diagnóstico e o painel do Discord continuam em português.
public static class L
{
    public static bool English { get; set; }

    private static string T(string pt, string en) => English ? en : pt;

    // Abas
    public static string TabOverview => T("Visão geral", "Overview");
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
    public static string LootToday => T("Loot de hoje", "Today's loot");
    public static string ColWater => T("Rega", "Watering");
    public static string AddField => T("Cadastrar o objeto selecionado", "Add the targeted object");
    public static string AtTime(string time) => T($"às {time}", $"at {time}");
    public static string SyncOff => T("Sincronia desligada", "Sync off");
    public static string SyncOk(string time) => T($"Sincronizado às {time}", $"Synced at {time}");
    public static string SyncProblem => T("Sem conexão com o serviço", "Can't reach the service");
    public static string Welcome => T("Bem-vindo ao FC Companion! Faltam só estes passos:", "Welcome to FC Companion! Just a few steps:");
    public static string StepCharacter => T("Ativar este personagem para ler os dados", "Enable this character to read data");
    public static string StepDiscord => T("Ligar o painel da FC no Discord (opcional)", "Connect the FC's Discord panel (optional)");
    public static string StepData => T("Abrir a lista de subs na oficina e o baú da FC", "Open the submarine list in the workshop and the FC chest");
    public static string EnableCharacter => T("Ativar", "Enable");
    public static string Done => T("Pronto, esconder", "Done, hide this");
    public static string Alerts => T("Avisos no jogo", "In-game alerts");
    public static string AlertSubs => T("Submarino voltou", "Submarine is back");
    public static string AlertWheels => T("Buff da mesa pronto", "Wheel buff ready");
    public static string AlertPlots => T("Plantação pode ser regada", "Garden can be watered");
    public static string AlertToast => T("Notificação na tela", "On-screen notification");
    public static string AlertChat => T("Mensagem no chat", "Chat message");
    public static string AlertSound => T("Som", "Sound");
    public static string AlertSoundId => T("Som do chat (1 a 16)", "Chat sound (1 to 16)");
    public static string Test => T("Testar", "Test");
    public static string ReplayWelcome => T("Testar boas-vindas", "Test welcome");
    public static string DevOnly => T("Só aparece na versão de dev.", "Only shown in the dev build.");
    public static string ColSub => T("Sub", "Sub");
    public static string ColRank => T("Rank", "Rank");
    public static string ColReturn => T("Volta em", "Back in");
    public static string ColParts => T("Peças", "Parts");
    public static string ColItem => T("Item", "Item");
    public static string ColCount => T("Qtd.", "Qty");
    public static string ColGil => T("Gil", "Gil");
    public static string ColBuff => T("Buff", "Buff");
    public static string ColReady => T("Pronto em", "Ready in");
    public static string ColEnds => T("Acaba em", "Ends in");
    public static string ColField => T("Campo", "Field");
    public static string ColState => T("Estado", "State");
    public static string ColName => T("Nome", "Name");
    public static string ColPlace => T("Local", "Place");
    public static string NoSupplies => T("Nenhum dado ainda. Abra a aba do baú da FC onde ficam os tanques.", "No data yet. Open the FC chest tab where the tanks are kept.");
    public static string Tanks(string count) => T($"Tanques de ceruleum: {count}", $"Ceruleum tanks: {count}");
    public static string Kits(string count) => T($"Kits de reparo: {count}", $"Repair materials: {count}");
    public static string Rounds(int rounds) => rounds == 1 ? T("≈ 1 rodada", "≈ 1 round") : T($"≈ {rounds} rodadas", $"≈ {rounds} rounds");
    public static string Repairs(int repairs, int subs) => repairs == 1 ? T($"≈ 1 reparo dos {subs}", $"≈ 1 repair of all {subs}") : T($"≈ {repairs} reparos dos {subs}", $"≈ {repairs} repairs of all {subs}");
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
    public static string NoPlots => T("Nenhum campo ainda. Selecione um canteiro ou vaso da casa e clique em +.", "No fields yet. Target a garden patch or pot and click +.");
    public static string Fields => T("Campos", "Fields");
    public static string AddFieldHint => T("Selecione um canteiro ou vaso da casa para cadastrar.", "Target a garden patch or pot to add it.");
    public static string FieldExists => T("Esse objeto já está na lista.", "This object is already listed.");
    public static string FieldName => T("Nome do campo", "Field name");
    public static string Save => T("Salvar", "Save");
    public static string Cancel => T("Cancelar", "Cancel");
    public static string NoWaterYet => T("sem rega ainda", "not watered yet");
    public static string Inside => T("dentro", "inside");
    public static string Outside => T("fora", "outside");
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
    public static string SendLogHint => T("Enviar log guarda o log no serviço da FC. Ele também vai sozinho logo depois de coletas, regas e mudanças no estoque, e ao fechar o jogo.", "Send log stores the log on the FC service. It is also sent automatically right after collections, watering and supply changes, and when the game closes.");

    // Chat e barra do jogo
    public static string SubReturned(string name) => T($"Submarino {name} voltou!", $"Submarine {name} is back!");
    public static string WheelReady(string name) => T($"{name} está pronta!", $"{name} is ready!");
    public static string PlotReady(string name) => T($"{name} já pode ser regado!", $"{name} can be watered now!");
    public static string WateredAll => T("Rega registrada em todos os plots.", "Watering recorded on every plot.");
    public static string DtrReady(int count) => count == 1 ? T("1 pronto", "1 ready") : T($"{count} prontos", $"{count} ready");
    public static string HelpMain => T("Abre a janela do FC Timers. Use \"/fct regar\" depois de regar a plantação.", "Opens the FC Timers window. Use \"/fct water\" after watering the garden.");
    public static string HelpShort => T("Atalho do /fctimers. \"/fct regar\" registra a rega da plantação.", "Shortcut for /fctimers. \"/fct water\" records the garden watering.");
}
