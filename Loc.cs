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
    public static string AlertWheels => T("Buff do Wheel Stand pronto", "Wheel Stand buff ready");
    public static string AlertPlots => T("Plantação pode ser regada", "Garden can be watered");
    public static string AlertToast => T("Notificação na tela", "On-screen notification");
    public static string AlertChat => T("Mensagem no chat", "Chat message");
    public static string AlertSound => T("Som", "Sound");
    public static string AlertSoundId => T("Som do chat (1 a 16)", "Chat sound (1 to 16)");
    public static string Test => T("Testar", "Test");
    public static string SendLogInactive => T("Só um personagem ativo envia o log. Ative este personagem nas configurações.", "Only an enabled character sends the log. Enable this character in the settings.");
    public static string ReplayWelcome => T("Testar boas-vindas", "Test welcome");
    public static string SupportKofi => T("Apoiar no Ko-fi", "Support on Ko-fi");
    public static string SupportHint => T("Abre o Ko-fi no navegador. Ajuda a pagar o serviço do Discord; nada no plugin é pago.", "Opens Ko-fi in your browser. It helps pay for the Discord service; nothing in the plugin is paid.");
    public static string SupportNotSet => T("O link do Ko-fi ainda não foi colocado (Support.KofiUrl).", "The Ko-fi link is not set yet (Support.KofiUrl).");
    public static string WhatsNew => T("Novidades", "What's new");
    public static string DevOnly => T("Só aparece na versão de dev.", "Only shown in the dev build.");
    public static string Airships => T("Airships", "Airships");
    public static string ColAirship => T("Airship", "Airship");
    public static string AlertAirships => T("Airship voltou", "Airship is back");
    public static string AirshipReturned(string name) => T($"Airship {name} voltou!", $"Airship {name} is back!");
    public static string House => T("Casa da FC", "FC house");
    public static string AlertHouse => T("Casa da FC sem visita (30, 40 e 44 dias)", "FC house not visited (30, 40 and 44 days)");
    public static string HouseUnknown => T("Abra a oficina uma vez, ou entre na casa da FC e clique em \"Esta é a casa da FC\".", "Open the workshop once, or enter the FC house and click \"This is the FC house\".");
    public static string HouseMark => T("Esta é a casa da FC", "This is the FC house");
    public static string HouseMarkHint => T("Marca a casa onde você está agora como a casa da FC.", "Marks the house you are in as the FC house.");
    public static string HouseMarkReplace => T("Você está em outra casa. Clique só se esta for a casa da FC: ela passa a valer no lugar da atual.", "You are in another house. Click only if this is the FC house: it replaces the current one.");
    public static string HouseNoVisit => T("Nenhuma entrada registrada ainda.", "No visit recorded yet.");
    public static string HouseVisited(string ago) => T($"Última visita há {ago}", $"Last visit {ago} ago");
    public static string HouseBy(string name) => T($"por {name}", $"by {name}");
    public static string HouseDemolish(string time) => T($"Derrubada em {time} sem visita", $"Demolished in {time} without a visit");
    public static string HouseHint => T("O jogo derruba a casa da FC depois de 45 dias sem nenhum membro entrar. Só conta quem usa o plugin.", "The game demolishes the FC house after 45 days without any member entering. Only members with the plugin are counted.");
    public static string HouseWarning(int days) => T($"Ninguém entra na casa da FC há {days} dias. Ela é derrubada aos 45!", $"Nobody has entered the FC house for {days} days. It is demolished at 45!");
    public static string EndsAround(string date) => T($"acaba ~{date}", $"runs out ~{date}");
    public static string Profit => T("Lucro (subs e airships)", "Profit (subs and airships)");
    public static string ColWeek => T("Semana", "Week");
    public static string ColVoyages => T("Viagens", "Voyages");
    public static string ColRoute => T("Rota", "Route");
    public static string ColPerVoyage => T("Gil por viagem", "Gil per voyage");
    public static string ColPerTank => T("Gil por tanque", "Gil per tank");
    public static string ProfitHint => T("Últimas 4 semanas, somando o que todos os membros enviaram ao serviço.", "Last 4 weeks, adding up what every member sent to the service.");
    public static string ColRank => T("Rank", "Rank");
    public static string ColReturn => T("Volta em", "Back in");
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
    public static string Rounds(int rounds) => rounds == 1 ? T("~ 1 rodada", "~ 1 round") : T($"~ {rounds} rodadas", $"~ {rounds} rounds");
    public static string Repairs(int repairs, int subs) => repairs == 1 ? T($"~ 1 reparo dos {subs}", $"~ 1 repair of all {subs}") : T($"~ {repairs} reparos dos {subs}", $"~ {repairs} repairs of all {subs}");
    public static string CountedNow => T("Contado agora, no baú da FC e no inventário.", "Counted just now, from the FC chest and inventory.");
    public static string CountedAgo(string time) => T($"Contado há {time}, no baú da FC e no inventário.", $"Counted {time} ago, from the FC chest and inventory.");

    // Loot
    public static string LootTitle => T("Loot por dia (valor de venda para NPC, dia de Brasília)", "Loot per day (NPC sell value, Brasília day)");
    public static string NoVoyages => T("Nenhuma viagem registrada ainda.", "No voyages recorded yet.");
    public static string Voyages(int count) => count == 1 ? T("1 viagem", "1 voyage") : T($"{count} viagens", $"{count} voyages");
    public static string DayTotal(string total, string voyages) => T($"{total} gil em {voyages}", $"{total} gil in {voyages}");
    public static string IncludesMarket(string value) => T($"inclui {value} gil de market", $"includes {value} gil from the market board");
    public static string Market => T("Preços do market", "Market prices");
    public static string MarketAuto(string world) => T($"Automático ({world})", $"Automatic ({world})");
    public static string MarketHint => T("Itens que valem mais no market contam pelo preço do market (média das vendas recentes no Universalis).", "Items worth more on the market board count at the market price (average of recent sales on Universalis).");

    // Buffs
    public static string Wheels => T("Wheel Stand", "Wheel Stand");
    public static string NoWheels => T("Nenhum buff registrado. Abra o Wheel Stand na casa da FC.", "No wheels recorded. Open the Wheel Stand in the FC house.");
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
    public static string WaterAllHint => T("ou /fcc regar. Cada plot também é registrado sozinho ao regar.", "or /fcc water. Each plot is also picked up on its own when watered.");
    public static string WaterIn(string time) => T($"regar em {time}", $"water in {time}");
    public static string WaterNow(string time) => T($"pronta para regar. Morre em {time}", $"ready to water. Dies in {time}");
    public static string WaterLate => T("passou do prazo de rega", "past the watering deadline");
    public static string WaterReady => T("pronta para regar", "ready to water");

    // Configurações
    public static string Language => T("Idioma", "Language");
    public static string Characters => T("Personagens que leem e enviam dados", "Characters that read and send data");
    public static string Current => T("atual", "current");
    public static string UnknownWorld => T("logue para ver o servidor", "log in to see the world");
    public static string OtherFc => T("outra FC", "other FC");
    public static string OtherFcHint => T(
        "Personagens de outra FC (alts de sub, por exemplo) não leem nem enviam nada, para não misturar com o painel da FC. Se a FC mudou de casa, entre na casa nova e use o botão \"Esta é a casa da FC\" na Visão geral.",
        "Characters in another FC (submarine alts, for example) don't read or send anything, so they don't mix with the FC panel. If the FC moved, go into the new house and use the \"This is the FC house\" button on the Overview.");
    public static string OtherFcNow => T(
        "Este personagem é de outra FC. O plugin não lê nem envia nada dele, para não misturar com o painel da FC.",
        "This character is in another FC. The plugin doesn't read or send anything from it, so it doesn't mix with the FC panel.");
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
    public static string HelpMain => T("Abre a janela do FC Companion. Use \"/fcc regar\" depois de regar a plantação.", "Opens the FC Companion window. Use \"/fcc water\" after watering the garden.");
    // Linha do tempo e cartões (UI-1)
    public static string NextPeriod(int days) => days == 1 ? T("Próximas 24 horas", "Next 24 hours") : T($"Próximos {days} dias", $"Next {days} days");
    public static string PeriodShort(int days) => days == 1 ? "24h" : T($"{days} dias", $"{days} days");
    public static string MoreMarkers(int count) => T($"e mais {count}", $"and {count} more");
    public static string Now => T("agora", "now");
    public static string NextUp(string what) => T($"Próximo: {what}", $"Next: {what}");
    public static string NothingNext => T("Nada marcado nas próximas 24 horas.", "Nothing in the next 24 hours.");
    public static string LaneHouse => T("Casa", "House");
    public static string LaneGarden => T("Rega", "Garden");
    public static string MarkReturns(string name) => T($"{name} volta", $"{name} returns");
    public static string MarkBack(string name) => T($"{name} voltou", $"{name} is back");
    public static string MarkWater(string name) => T($"{name} pode ser regado", $"{name} can be watered");
    public static string MarkWaterNow(string name) => T($"{name} já pode ser regado", $"{name} can be watered now");
    public static string MarkDies(string name) => T($"{name} morre sem rega", $"{name} dies without water");
    public static string MarkWheel(string name) => T($"{name} pronto", $"{name} ready");
    public static string MarkBuffEnds(string name) => T($"{name} acaba", $"{name} ends");
    public static string MarkHouse => T("Casa da FC é derrubada sem visita", "FC house is demolished without a visit");
    public static string InTime(string time) => T($"em {time}", $"in {time}");
    public static string InDays(int days) => T($"em {days} d", $"in {days} d");
    public static string Hull => T("Casco", "Hull");
    public static string Stern => T("Popa", "Stern");
    public static string Bow => T("Proa", "Bow");
    public static string Bridge => T("Ponte", "Bridge");
    public static string PartsNotRead => T("Peças ainda não lidas neste PC. Abra a lista de subs na oficina.", "Parts not read on this PC yet. Open the submarine list in the workshop.");
    public static string BestItem => T("Melhor item", "Best item");
}
