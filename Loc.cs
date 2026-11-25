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
    public static string StepCharacter => T("Ativar este personagem para ler os dados", "Enable this character so it can read data");
    public static string StepDiscord => T("Ligar o painel da FC no Discord (opcional)", "Connect the FC's Discord panel (optional)");
    public static string StepData => T("Abrir a lista de submarinos na oficina e o baú da FC", "Open the submarine list in the workshop and the FC chest");
    public static string EnableCharacter => T("Ativar", "Enable");
    public static string Done => T("Pronto, esconder", "Done, hide this");
    public static string Alerts => T("Avisos no jogo", "In-game alerts");
    public static string AlertSubs => T("Submarino voltou", "Submarine is back");
    public static string AlertWheels => T("Buff do Wheel Stand pronto", "Wheel Stand buff ready");
    public static string AlertPlots => T("Campo pronto para regar", "Field ready to water");
    public static string AlertToast => T("Notificação na tela", "On-screen notification");
    public static string AlertChat => T("Mensagem no chat", "Chat message");
    public static string AlertSound => T("Som", "Sound");
    public static string AlertSoundId => T("Som do chat (1 a 16)", "Chat sound (1 to 16)");
    public static string Test => T("Testar", "Test");
    public static string SendLogInactive => T("Só um personagem ativo envia o log. Ative este personagem nas configurações.", "Only an enabled character sends the log. Enable this character in Settings.");
    public static string ReplayWelcome => T("Testar boas-vindas", "Test welcome");
    public static string SupportKofi => T("Apoiar no Ko-fi", "Support on Ko-fi");
    public static string SupportHint => T("Abre o Ko-fi no navegador.", "Opens Ko-fi in your browser.");
    public static string SupportNotSet => T("O link do Ko-fi ainda não foi colocado (Support.KofiUrl).", "The Ko-fi link is not set yet (Support.KofiUrl).");
    public static string WhatsNewIn(string version) => T($"Novidades da {version}", $"What's new in {version}");
    public static string WhatsNewNext => T("Novidades da próxima versão", "What's new in the next version");
    public static string EarlierVersions => T("Versões anteriores", "Earlier versions");
    public static string ChangesUnreadable => T("Não deu para ler a lista de novidades.", "The list of changes could not be read.");
    public static string Close => T("Fechar", "Close");
    public static string TypeAdded => T("Novo", "New");
    public static string TypeChanged => T("Mudou", "Changed");
    public static string TypeFixed => T("Corrigido", "Fixed");
    public static string TypeRemoved => T("Removido", "Removed");
    public static string TypeOther => T("Outro", "Other");
    // Ligação por código
    public static string LinkTitle => T("Ligar à FC", "Link to your FC");
    public static string LinkHint => T("Código de /fcc → Ajustes, no Discord.", "From /fcc → Manage, on Discord.");
    public static string LinkCode => T("Código da FC", "FC code");
    public static string LinkCheck => T("Conferir", "Check");
    public static string LinkConfirm(string fc, string server) => server.Length > 0 ? T($"Ligar à FC {fc} no servidor {server}?", $"Link to the FC {fc} on the {server} server?") : T($"Ligar à FC {fc}?", $"Link to the FC {fc}?");
    public static string LinkYes => T("Ligar", "Link");
    public static string LinkNeedsFcId => T("Abra a janela da Free Company uma vez.", "Open the Free Company window once.");
    public static string LinkedTo(string fc, string server) => server.Length > 0 ? T($"Ligado à FC {fc} ({server})", $"Linked to the FC {fc} ({server})") : T($"Ligado à FC {fc}", $"Linked to the FC {fc}");
    public static string LinkAccountHint => T("Código de /fcc → Eventos → Ligar minha conta.", "From /fcc → Events → Link my account.");
    public static string LinkAccountCode => T("Código da conta", "Account code");
    public static string LinkAccount => T("Ligar minha conta", "Link my account");
    public static string LinkLeave => T("Desligar desta FC", "Unlink from this FC");
    public static string LinkLeaveSure => T("Clique de novo para desligar", "Click again to unlink");
    public static string Advanced => T("Avançado: endereço e chave antiga", "Advanced: address and old key");
    public static string LinkWorking => T("Falando com o serviço...", "Talking to the service...");
    public static string LinkDone => T("Ligado! O plugin já envia e recebe os dados da FC.", "Linked! The plugin now sends and receives the FC data.");
    public static string LinkLeft => T("Desligado da FC.", "Unlinked from the FC.");
    public static string AccountLinked => T("Conta do Discord ligada.", "Discord account linked.");
    public static string LinkUnknownCode => T("Código não encontrado. Confira ou peça um novo.", "Code not found. Check it or ask for a new one.");
    public static string LinkOtherFc(string fc) => T($"Esse código é da FC {fc}, não da FC deste personagem.", $"This code belongs to the FC {fc}, not this character's FC.");
    public static string LinkTooMany => T("Muitas tentativas erradas. Tente de novo daqui a uma hora.", "Too many wrong tries. Try again in an hour.");
    public static string LinkInvalid => T("O serviço não aceitou os dados. Mande o log para o Luciano.", "The service didn't accept the data. Send the log.");
    public static string LinkExpired => T("A ligação não vale mais. Cole o código da FC de novo.", "The link is no longer valid. Paste the FC code again.");
    public static string LinkOffline => T("Não deu para falar com o serviço agora.", "Couldn't reach the service right now.");
    public static string LinkFailed(int code) => T($"Erro {code} do serviço.", $"Service error {code}.");
    public static string WhatsNew => T("Novidades", "What's new");
    public static string DevOnly => T("Só aparece na versão de dev.", "Only shown in the dev build.");
    public static string Airships => T("Airships", "Airships");
    public static string ColAirship => T("Airship", "Airship");
    public static string AlertAirships => T("Airship voltou", "Airship is back");
    public static string AirshipReturned(string name) => T($"Airship {name} voltou!", $"Airship {name} is back!");
    public static string House => T("Casa da FC", "FC house");
    public static string AlertHouse => T("Casa da FC sem visita (30, 40 e 44 dias)", "FC house without a visit (30, 40 and 44 days)");
    public static string HouseUnknown => T("Abra a oficina uma vez, ou entre na casa da FC e clique em \"Esta é a casa da FC\".", "Open the workshop once, or enter the FC house and click \"This is the FC house\".");
    public static string HouseMark => T("Esta é a casa da FC", "This is the FC house");
    public static string HouseMarkReplace => T("Substitui a casa da FC salva.", "Replaces the saved FC house.");
    public static string HouseNoVisit => T("Nenhuma entrada registrada ainda.", "No visit recorded yet.");
    public static string HouseVisited(string ago) => T($"Última visita há {ago}", $"Last visit {ago} ago");
    public static string HouseBy(string name) => T($"por {name}", $"by {name}");
    public static string HouseDemolish(string time) => T($"Vai ser derrubada em {time} sem visita", $"Will be demolished in {time} without a visit");
    public static string HouseWarning(int days) => T($"Ninguém entra na casa da FC há {days} dias. Ela é derrubada aos 45!", $"Nobody has been in the FC house for {days} days. It gets demolished at 45!");
    public static string EndsAround(string date) => T($"acabam por volta de {date}", $"run out around {date}");
    public static string Profit => T("Lucro (submarinos e airships)", "Profit (submarines and airships)");
    public static string ColWeek => T("Semana", "Week");
    public static string ColVoyages => T("Viagens", "Voyages");
    public static string ColRoute => T("Rota", "Route");
    public static string ColPerVoyage => T("Gil por viagem", "Gil per voyage");
    public static string ColPerTank => T("Gil por tanque", "Gil per tank");
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
    public static string Kits(string count) => T($"Materiais de reparo: {count}", $"Repair materials: {count}");
    public static string Rounds(int rounds) => rounds switch
    {
        0 => T("nenhuma rodada", "no rounds"),
        1 => T("1 rodada", "1 round"),
        _ => T($"{rounds} rodadas", $"{rounds} rounds"),
    };
    public static string Repairs(int repairs) => repairs switch
    {
        0 => T("nenhum reparo", "no repairs"),
        1 => T("1 reparo", "1 repair"),
        _ => T($"{repairs} reparos", $"{repairs} repairs"),
    };

    public static string RepairsHint(int perRepair, int subs) => T($"Um reparo dos {subs} submarinos usa {perRepair} materiais.", $"Repairing all {subs} submarines takes {perRepair} materials.");
    public static string RoundsHint(int perRound, int subs) => T($"Uma rodada dos {subs} submarinos usa {perRound} tanques.", $"One round of all {subs} submarines takes {perRound} tanks.");
    public static string CountedNow => T("Contado agora no baú e no inventário", "Counted just now in the chest and inventory");
    public static string CountedAgo(string time) => T($"Contado há {time} no baú e no inventário", $"Counted {time} ago in the chest and inventory");

    // Loot
    public static string LootTitle => T("Loot por dia", "Loot per day");
    public static string NoVoyages => T("Nenhuma viagem registrada ainda.", "No voyages recorded yet.");
    public static string Voyages(int count) => count == 1 ? T("1 viagem", "1 voyage") : T($"{count} viagens", $"{count} voyages");
    public static string About(string value) => T($"cerca de {value}", $"about {value}");
    public static string DayTotal(string total, string voyages) => T($"{total} gil em {voyages}", $"{total} gil in {voyages}");
    public static string IncludesMarket(string value) => T($"inclui {value} gil de market", $"includes {value} gil from the market board");
    public static string Market => T("Preços do market", "Market prices");
    public static string MarketAuto(string world) => T($"Automático ({world})", $"Automatic ({world})");

    // Buffs
    public static string Wheels => T("Wheel Stand", "Wheel Stand");
    public static string NoWheels => T("Nenhuma roda registrada. Abra o Wheel Stand na casa da FC.", "No wheels recorded yet. Open the Wheel Stand in the FC house.");
    public static string ActiveBuffs => T("Buffs ativos", "Active buffs");
    public static string None => T("Nenhum", "None");

    // Plantação
    public static string NoPlots => T("Nenhum campo ainda. Selecione um canteiro ou vaso da casa e clique em +.", "No fields yet. Target a garden patch or pot and press +.");
    public static string Fields => T("Campos", "Fields");
    public static string AddFieldHint => T("Selecione um canteiro ou vaso.", "Target a garden patch or pot.");
    public static string FieldExists => T("Esse objeto já está na lista.", "That object is already on the list.");
    public static string FieldName => T("Nome do campo", "Field name");
    public static string Save => T("Salvar", "Save");
    public static string Cancel => T("Cancelar", "Cancel");
    public static string NoWaterYet => T("sem rega ainda", "not watered yet");
    public static string Inside => T("dentro", "inside");
    public static string Outside => T("fora", "outside");
    public static string Remove => T("Remover", "Remove");
    public static string WaterAll => T("Reguei todos agora", "Watered all now");
    public static string WaterIn(string time) => T($"regar em {time}", $"water in {time}");
    public static string WaterNow(string time) => T($"pode regar. Morre em {time}", $"ready to water. Dies in {time}");
    public static string WaterLate => T("passou do prazo de rega", "past the watering deadline");
    public static string WaterReady => T("pode regar", "ready to water");

    // Configurações
    public static string Language => T("Idioma", "Language");
    public static string Characters => T("Personagens que leem e enviam dados", "Characters that read and send data");
    public static string Current => T("atual", "current");
    public static string UnknownWorld => T("entre no personagem para ver o mundo", "log in to see the world");
    public static string OtherFc => T("outra FC", "other FC");
    public static string OtherFcNow => T("Personagem de outra FC: nada é lido nem enviado.", "Character in another FC: nothing is read or sent.");
    public static string SyncEnabled => T("Enviar e receber os dados do painel da FC", "Send and receive the FC panel data");
    public static string ServiceUrl => T("Endereço do serviço", "Service address");
    public static string ServiceKey => T("Chave da FC", "FC key");
    public static string Sent(string status) => T($"Envio: {status}", $"Sending: {status}");
    public static string Received(string time) => T($"Recebido: {time}", $"Received: {time}");
    public static string NothingReceived => T("Recebido: nada ainda", "Received: nothing yet");
    public static string Diagnostics => T("Diagnóstico", "Diagnostics");
    public static string DetailedLog => T("Log detalhado", "Detailed log");
    public static string CopyLog => T("Copiar log", "Copy log");
    public static string SendLog => T("Enviar log", "Send log");
    public static string Copied => T("Copiado. Cole no Discord.", "Copied. Paste it in Discord.");

    // Chat e barra do jogo
    public static string SubReturned(string name) => T($"Submarino {name} voltou!", $"Submarine {name} is back!");
    public static string WheelReady(string name) => T($"{name} está pronto!", $"{name} is ready!");
    public static string PlotReady(string name) => T($"{name} já pode ser regado!", $"{name} can be watered now!");
    public static string WateredAll => T("Rega registrada em todos os campos.", "Watering recorded for every field.");
    public static string DtrReady(int count) => count == 1 ? T("1 pronto", "1 ready") : T($"{count} prontos", $"{count} ready");

    public static string HelpMain => T("Abre o FC Companion.", "Opens FC Companion.");

    // Linha do tempo e cartões
    public static string NextPeriod(int days) => days == 1 ? T("Próximas 24 horas", "Next 24 hours") : T($"Próximos {days} dias", $"Next {days} days");
    public static string PeriodShort(int days) => days == 1 ? "24h" : T($"{days} dias", $"{days} days");
    public static string MoreMarkers(int count) => T($"e mais {count}", $"and {count} more");
    public static string Now => T("agora", "now");
    public static string NextUp(string what) => T($"Próximo: {what}", $"Next: {what}");
    public static string NothingNext => T("Nada marcado neste período.", "Nothing coming up in this period.");
    public static string LaneHouse => T("Casa", "House");
    public static string LaneGarden => T("Rega", "Watering");
    public static string MarkReturns(string name) => T($"{name} volta", $"{name} returns");
    public static string MarkBack(string name) => T($"{name} voltou", $"{name} is back");
    public static string MarkWater(string name) => T($"{name} pode ser regado", $"{name} can be watered");
    public static string MarkWaterNow(string name) => T($"{name} já pode ser regado", $"{name} can be watered now");
    public static string MarkDies(string name) => T($"{name} morre sem rega", $"{name} dies without water");
    public static string MarkWheel(string name) => T($"{name} pronto", $"{name} is ready");
    public static string MarkBuffEnds(string name) => T($"{name} acaba", $"{name} ends");
    public static string MarkHouse => T("Prazo da casa da FC (derrubada sem visita)", "FC house deadline (demolished if nobody visits)");
    public static string InTime(string time) => T($"em {time}", $"in {time}");
    public static string Days(int days) => days == 1 ? T("1 dia", "1 day") : T($"{days} dias", $"{days} days");
    public static string InDays(int days) => T($"em {days} dias", $"in {days} days");
    public static string Hull => T("Casco", "Hull");
    public static string Stern => T("Popa", "Stern");
    public static string Bow => T("Proa", "Bow");
    public static string Bridge => T("Ponte", "Bridge");
    public static string PartsNotRead => T("Peças ainda não lidas neste PC. Abra a lista de subs na oficina.", "Parts haven't been read on this PC yet. Open the submarine list in the workshop.");
    public static string BestItem => T("Melhor item do dia", "Best item of the day");
}
