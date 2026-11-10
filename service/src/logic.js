// Regras do painel, sem dependência do Cloudflare. Ver ../../REGRAS.md.
export const TIME_ZONE = 'America/Sao_Paulo';
const MAX_ITEMS_SHOWN = 8;
const MAX_NAME_SHOWN = 60;
const END_TIME_TOLERANCE = 120;
const ALERT_LIFETIME = 8 * 3600;
const REMINDER_LEAD = 3600;
const REMINDER_GROUP = 900;
const REMINDER_MIN_LEFT = 1800;
// Avisos de que as plantas vão morrer: 3, 2 e 1 hora antes.
const DEATH_WARNINGS = [3, 2, 1].map((h) => h * 3600);
const DEATH_WARNING_LATE = 1800;
// Plot que morreu há mais de 2 dias some do painel e dos avisos.
const PLOT_STALE = 2 * 86400;
const MAX_PLOTS = 64;
const MAX_PLOT_LINES = 12;
const DAY_STEP = 900;
export const HISTORY_DAYS = 7;
// A lista de itens fica fora do painel para a mensagem do webhook ficar leve. O código continua aqui para um bot no futuro.
export const SHOW_ITEMS = false;
const MAX_SUBMARINES = 4;
// Abaixo disso sai um aviso de estoque baixo, uma vez, até o estoque voltar a subir.
// Com as rotas conhecidas, o limite de tanques é de 2 rodadas; sem elas, 120 tanques.
export const LOW_TANKS = 120;
export const LOW_ROUNDS = 2;
export const LOW_KITS = 20;
// Item que vale mais no market do que no NPC gera aviso a partir deste valor.
export const MARKET_ALERT = 100000;
export const MARKET_CACHE = 6 * 3600;
// Sub pronto há 2 horas sem sair de novo ganha um lembrete. Um vaso fica "esquecido" quando outro vaso
// da mesma casa foi regado há 15 minutos ou mais depois de ele já poder ser regado.
export const FORGOTTEN_SUB = 2 * 3600;
export const FORGOTTEN_PLOT = 900;
// A cada 30 minutos o serviço confere se o Dalamud já saiu para a versão atual do jogo.
export const DALAMUD_CHECK = 1800;
// Desgaste da peça mais gasta: abaixo de 50% aparece no painel, abaixo de 30% sai o aviso de reparo.
export const REPAIR_SHOW = 50;
export const REPAIR_LOW = 30;

// Até a versão 0.6.0 o plugin enviava uma 5ª posição, que repete um dos 4 submarinos.
export const isRealSlot = (slot) => Number(slot) < MAX_SUBMARINES;

const dayFormat = new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit' });
const gilFormat = new Intl.NumberFormat('pt-BR');

export const newState = () => ({
  subs: {}, buffs: {}, buffsObservedAt: 0, activeBuffs: null, plots: {}, supplies: null, notices: [], alerts: [], updatedAt: 0, messageId: null, lastPanel: null,
});

// O lembrete sai 1 hora antes. Se o serviço só puder enviar com menos de 30 minutos restando, ele não sai.
const reminderDue = (time, reminded, now) => reminded !== time && now >= time - REMINDER_LEAD && now < time - REMINDER_MIN_LEFT;

// Avisos que precisam sair agora, já agrupados. Cada submarino e cada rega avisa uma vez só.
export function pendingAlerts(state, now) {
  const subs = Object.values(state.subs).filter((s) => s.returnTime > 0);
  const alerts = [];

  // Subs que voltam até 15 minutos um do outro saem num aviso só, quando o último voltar.
  const ready = waitGroup(subs.filter((s) => s.notified !== s.returnTime), (s) => s.returnTime, now);
  if (ready) alerts.push({ kind: 'subReady', subs: ready });

  // Quem volta até 15 minutos depois entra no mesmo lembrete.
  if (subs.some((s) => reminderDue(s.returnTime, s.reminded, now))) {
    const soon = subs.filter((s) => s.reminded !== s.returnTime
      && s.returnTime > now + REMINDER_MIN_LEFT && s.returnTime <= now + REMINDER_LEAD + REMINDER_GROUP);
    alerts.push({ kind: 'subReminder', subs: soon });
  }

  // Plantas: cada plot avisa por conta própria, e os que ficam prontos juntos entram no mesmo aviso.
  const plots = activePlots(state, now);
  const readyPlots = plots.filter((p) => p.nextWaterAt <= now && p.notified !== p.nextWaterAt);
  if (readyPlots.length > 0) alerts.push({ kind: 'plotReady', plots: readyPlots });

  if (plots.some((p) => reminderDue(p.nextWaterAt, p.reminded, now))) {
    const soon = plots.filter((p) => p.reminded !== p.nextWaterAt
      && p.nextWaterAt > now + REMINDER_MIN_LEFT && p.nextWaterAt <= now + REMINDER_LEAD + REMINDER_GROUP);
    alerts.push({ kind: 'plotReminder', plots: soon });
  }

  const dying = plots.map((p) => ({ ...p, step: deathWarningDue(p, now) })).filter((p) => p.step);
  if (dying.length > 0) alerts.push({ kind: 'plotDying', plots: dying });

  const wheels = Object.values(state.buffs).filter((b) => b.readyTime > 0 && b.readyTime <= now && b.notified !== b.readyTime);
  if (wheels.length > 0) alerts.push({ kind: 'wheelReady', wheels });

  const parked = subs.filter((s) => s.notified === s.returnTime && s.forgotten !== s.returnTime);
  const late = waitGroup(parked, (s) => s.returnTime + FORGOTTEN_SUB, now);
  if (late) alerts.push({ kind: 'subForgotten', subs: late });

  const forgotten = plots.filter((p) => isForgotten(p, plots, now));
  if (forgotten.length > 0) alerts.push({ kind: 'plotForgotten', plots: forgotten });

  const worn = subs.filter((s) => needsRepair(s) && !s.repairWarned);
  if (worn.length > 0) alerts.push({ kind: 'repairNeeded', subs: worn, supplies: state.supplies });

  const supplies = state.supplies;
  if (supplies && !supplies.lowWarned && isLow(supplies, state)) {
    const known = Object.values(state.subs).filter((s) => s.details?.tanks > 0);
    const perRound = tanksPerRound(state);
    alerts.push({ kind: 'suppliesLow', supplies, lowTanks: lowTanksOf(state), perRound, perVoyage: known.length > 0 ? perRound / known.length : 0 });
  }

  // Avisos de uma vez só que nascem de uma viagem: item caro de market e setor novo.
  for (const notice of state.notices ?? []) alerts.push({ kind: notice.kind, notice });

  return alerts;
}

// O grupo começa no primeiro que já chegou na hora e inclui quem chega até 15 minutos depois.
// Só sai quando o último do grupo chegar, então quem chega junto nunca gera um aviso para cada um.
function waitGroup(items, timeOf, now) {
  const due = items.filter((i) => timeOf(i) <= now);
  if (due.length === 0) return null;
  const start = Math.min(...due.map(timeOf));
  const group = items.filter((i) => timeOf(i) >= start && timeOf(i) <= start + REMINDER_GROUP);
  return group.every((i) => timeOf(i) <= now) ? group : null;
}

// Avisos de sub dos últimos 8 horas, agrupados de novo, para trocar avisos repetidos no canal por um só.
export function recentSubAlerts(state, now) {
  const subs = Object.values(state.subs).filter((s) => s.returnTime > 0 && s.returnTime > now - ALERT_LIFETIME);
  const groups = (list, timeOf) => {
    const result = [];
    let pending = [...list].filter((s) => timeOf(s) <= now).sort((a, b) => timeOf(a) - timeOf(b));
    while (pending.length > 0) {
      const start = timeOf(pending[0]);
      result.push(pending.filter((s) => timeOf(s) <= start + REMINDER_GROUP));
      pending = pending.filter((s) => timeOf(s) > start + REMINDER_GROUP);
    }
    return result;
  };
  return [
    ...groups(subs.filter((s) => s.notified === s.returnTime), (s) => s.returnTime).map((g) => ({ kind: 'subReady', subs: g })),
    ...groups(subs.filter((s) => s.forgotten === s.returnTime), (s) => s.returnTime + FORGOTTEN_SUB).map((g) => ({ kind: 'subForgotten', subs: g })),
  ];
}

// Só vasos de dentro de casa têm nome confiável; os canteiros de fora são regados todos juntos.
const houseOf = (key) => key.split(':').slice(0, 4).join(':');
function isForgotten(plot, plots, now) {
  if (!plot.indoor || plot.nextWaterAt > now || plot.forgotten === plot.nextWaterAt) return false;
  return plots.some((other) => other.key !== plot.key && other.indoor && houseOf(other.key) === houseOf(plot.key)
    && other.observedAt > plot.nextWaterAt && other.observedAt <= now - FORGOTTEN_PLOT);
}

const needsRepair = (sub) => sub.details?.condition != null && sub.details.condition < REPAIR_LOW;

const tanksPerRound = (state) => Object.values(state.subs).reduce((sum, s) => sum + (s.details?.tanks ?? 0), 0);
const lowTanksOf = (state) => (tanksPerRound(state) > 0 ? LOW_ROUNDS * tanksPerRound(state) : LOW_TANKS);
const isLow = (supplies, state) => supplies.tanks < lowTanksOf(state) || supplies.kits < LOW_KITS;

// Plots com horário de rega, sem os que morreram há muito tempo ou foram retirados.
const plotKey = (key) => key.replace(/,/g, '.');

// "2026.11.03.0000.0000" vira "03/11".
const patchDay = (version) => {
  const [, month, day] = (version ?? '').split('.');
  return month && day ? `${day}/${month}` : version;
};

// Guarda a versão do jogo e a que o Dalamud suporta. Quando um patch sai ou o Dalamud alcança o patch,
// sai um aviso de uma vez só. Na primeira conferência só guarda, sem avisar.
export function applyDalamud(state, supportedGame, latestGame, now) {
  const outdated = supportedGame !== latestGame;
  const previous = state.dalamud;
  state.dalamud = { supportedGame, latestGame, outdated, checkedAt: now };
  if (!previous || previous.outdated === outdated) return;
  state.notices ??= [];
  state.notices.push(outdated
    ? { id: `dalamud:out:${latestGame}`, kind: 'dalamudOutdated', latestGame }
    : { id: `dalamud:ok:${latestGame}`, kind: 'dalamudReady', latestGame });
}

export const dalamudDue = (state, now) => !state.dalamud || now - state.dalamud.checkedAt >= DALAMUD_CHECK;

function dalamudLine(dalamud) {
  if (!dalamud) return null;
  return dalamud.outdated
    ? `🧩 ⚠️ Dalamud ainda não saiu para o patch de ${patchDay(dalamud.latestGame)}: plugins parados`
    : '🧩 ✅ Dalamud em dia com o jogo';
}

// O plot geral ("Plantação") vem de regas que não sabiam qual canteiro foi. Some quando algum canteiro
// identificado é regado depois dele.
export function activePlots(state, now) {
  const all = Object.entries(state.plots ?? {}).map(([key, plot]) => ({ key, ...plot }));
  const general = all.find((p) => p.key === 'geral');
  const superseded = general && all.some((p) => p.key !== 'geral' && p.observedAt > general.observedAt);
  return all
    .filter((p) => !(superseded && p.key === 'geral'))
    .filter((p) => p.nextWaterAt > 0 && !(p.dieAt > 0 && p.dieAt < now - PLOT_STALE))
    .sort((a, b) => a.nextWaterAt - b.nextWaterAt || a.name.localeCompare(b.name));
}

// O aviso de morte da vez: o último horário de aviso que já passou, se ainda não saiu e não ficou velho.
function deathWarningDue(plot, now) {
  if (!(plot.dieAt > now)) return null;
  const step = DEATH_WARNINGS.map((before) => plot.dieAt - before).filter((t) => t <= now).at(-1);
  if (!step || step <= (plot.warned ?? 0) || now - step >= DEATH_WARNING_LATE) return null;
  return step;
}

// Marca no estado que o aviso saiu. Só marca o que ainda está igual ao que foi avisado.
export function markSent(state, alert) {
  for (const sent of alert.subs ?? []) {
    if (alert.kind === 'repairNeeded') {
      const worn = Object.values(state.subs).find((s) => s.name === sent.name);
      if (worn) worn.repairWarned = true;
      continue;
    }
    const sub = Object.values(state.subs).find((s) => s.name === sent.name && s.returnTime === sent.returnTime);
    if (!sub) continue;
    if (alert.kind === 'subReady') sub.notified = sub.returnTime;
    else if (alert.kind === 'subForgotten') sub.forgotten = sub.returnTime;
    else sub.reminded = sub.returnTime;
  }
  for (const sent of alert.wheels ?? []) {
    const wheel = Object.values(state.buffs).find((b) => b.name === sent.name && b.readyTime === sent.readyTime);
    if (wheel) wheel.notified = wheel.readyTime;
  }
  for (const sent of alert.plots ?? []) {
    const plot = state.plots?.[sent.key];
    if (!plot) continue;
    if (alert.kind === 'plotReady' && plot.nextWaterAt === sent.nextWaterAt) plot.notified = plot.nextWaterAt;
    if (alert.kind === 'plotReminder' && plot.nextWaterAt === sent.nextWaterAt) plot.reminded = plot.nextWaterAt;
    if (alert.kind === 'plotDying' && plot.dieAt === sent.dieAt) plot.warned = sent.step;
    if (alert.kind === 'plotForgotten' && plot.nextWaterAt === sent.nextWaterAt) plot.forgotten = plot.nextWaterAt;
  }
  if (alert.kind === 'suppliesLow' && state.supplies) state.supplies.lowWarned = true;
  if (alert.notice) state.notices = (state.notices ?? []).filter((n) => n.id !== alert.notice.id);
}

export const alertDeleteAt = (sentAt) => sentAt + ALERT_LIFETIME;

const joinList = (names) => (names.length > 1 ? `${names.slice(0, -1).join(', ')} e ${names.at(-1)}` : names[0]);
const joinNames = (list) => joinList(list.map((s) => `**${cleanName(s.name)}**`));
const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;

// Nomes só dos vasos de dentro de casa. Os canteiros de fora são regados juntos, então não vale listar.
function plotNames(plots) {
  const names = plots.filter((p) => p.indoor).map((p) => `**${p.name}**`);
  if (names.length === 0) return '';
  if (plots.some((p) => !p.indoor)) names.push('os canteiros de fora');
  return ` (${joinList(names)})`;
}

export function alertPayload(alert, roleId) {
  const texts = {
    subReady: () => `🚢 ${joinNames(alert.subs)} ${alert.subs.length > 1 ? 'voltaram' : 'voltou'}!`,
    subReminder: () => `Lembrete: Em 1 hora os submarinos estarão prontos para coleta (${joinNames(alert.subs)}).`,
    plotReady: () => `🌱 A plantação já pode ser regada${plotNames(alert.plots)}!`,
    plotReminder: () => `Lembrete: Em 1 hora a plantação estará disponível para regar${plotNames(alert.plots)}.`,
    plotForgotten: () => `🌱 ${joinNames(alert.plots)} ${alert.plots.length > 1 ? 'ficaram' : 'ficou'} sem regar? Os outros vasos já foram regados.`,
    wheelReady: () => `⚙️ ${joinNames(alert.wheels)} ${alert.wheels.length > 1 ? 'estão prontos' : 'está pronto'}!`,
    subForgotten: () => `⏰ ${joinNames(alert.subs)} ${alert.subs.length > 1 ? 'voltaram' : 'voltou'} há mais de 2 horas e ainda não ${alert.subs.length > 1 ? 'saíram' : 'saiu'} de novo.`,
    repairNeeded: () => {
      const subs = joinList(alert.subs.map((s) => `**${s.name}** (${percent(s.details.condition)})`));
      const kits = alert.subs.reduce((sum, s) => sum + (s.details.kits ?? 0), 0);
      const stock = alert.supplies ? `, ${gilFormat.format(alert.supplies.kits)} no estoque` : '';
      return `🔧 Hora de reparar: ${subs}. O reparo completo gasta ${gilFormat.format(kits)} kits${stock}.`;
    },
    suppliesLow: () => {
      const low = [];
      if (alert.supplies.tanks < alert.lowTanks) {
        const lasts = alert.perVoyage > 0
          ? ` (≈ ${plural(Math.floor(alert.supplies.tanks / alert.perVoyage), 'viagem', 'viagens')}, ${plural(Math.floor(alert.supplies.tanks / alert.perRound), 'rodada', 'rodadas')})`
          : '';
        low.push(`**${gilFormat.format(alert.supplies.tanks)} tanques de ceruleum**${lasts}`);
      }
      if (alert.supplies.kits < LOW_KITS) low.push(`**${gilFormat.format(alert.supplies.kits)} kits de reparo**`);
      return `⛽ Estoque baixo: restam ${joinList(low)}.`;
    },
    marketDrop: () => {
      const { notice } = alert;
      const items = joinList(notice.items.map((i) => `${i.count}x ${i.name} ≈ ${gilFormat.format(i.marketValue)} gil`));
      return `💎 **${notice.subName}** trouxe item de market: ${items} (${notice.world}).`;
    },
    dalamudOutdated: () => `🧩 Patch novo do jogo (${patchDay(alert.notice.latestGame)}): os plugins, incluindo o FC Timers, param até o Dalamud atualizar.`,
    dalamudReady: () => `🧩 O Dalamud saiu para o patch de ${patchDay(alert.notice.latestGame)}. O FC Timers volta a funcionar ao abrir o jogo.`,
    sectorUnlocked: () => `🗺️ **${alert.notice.subName}** desbloqueou ${joinList(alert.notice.sectors.map((s) => `**${s}**`))}!`,
    plotDying: () => {
      const named = alert.plots.filter((p) => p.indoor).map((p) => `**${p.name}** <t:${p.dieAt}:R>`);
      const outside = alert.plots.filter((p) => !p.indoor);
      const outsideDies = Math.min(...outside.map((p) => p.dieAt));
      if (named.length === 0) return `⚠️ Se ninguém regar, a plantação morre <t:${outsideDies}:R>!`;
      if (outside.length > 0) named.push(`os canteiros de fora <t:${outsideDies}:R>`);
      return `⚠️ Se ninguém regar, ${alert.plots.length > 1 ? 'morrem' : 'morre'}: ${joinList(named)}!`;
    },
  };
  const text = texts[alert.kind]();
  return {
    content: roleId ? `<@&${roleId}> ${text}` : text,
    allowed_mentions: { parse: [], roles: roleId ? [roleId] : [] },
  };
}

export const dayOf = (unix) => dayFormat.format(new Date(unix * 1000));

export const voyageKey = (voyage) => `voyage:${dayOf(voyage.returnTime)}:${voyage.slot}:${voyage.returnTime}`;

const percent = (value) => `${Math.floor(value)}%`;

const isSlot = (v) => Number.isInteger(v) && v >= 0 && v < 32;
const isTime = (v) => Number.isInteger(v) && v >= 0;
const isName = (v) => typeof v === 'string' && v.length > 0 && v.length <= 128;
const isAge = (v) => Number.isFinite(v) && v >= 0;

function isVoyage(v) {
  return v && isSlot(v.slot) && isName(v.subName) && isTime(v.returnTime) && v.returnTime > 0
    && Array.isArray(v.items) && v.items.length > 0 && v.items.length <= 16
    && v.items.every((i) => i && isTime(i.itemId) && isName(i.name) && isTime(i.count) && isTime(i.value)
      && (i.market == null || typeof i.market === 'boolean'))
    && (v.world == null || (typeof v.world === 'string' && v.world.length <= 32))
    && (v.unlocked == null || (Array.isArray(v.unlocked) && v.unlocked.length <= 5 && v.unlocked.every(isName)));
}

const isShort = (v) => typeof v === 'string' && v.length <= 32;

function isDetails(d) {
  return isTime(d.rank) && d.rank < 256 && isTime(d.exp) && d.exp <= 100 && isShort(d.build) && isShort(d.route)
    && isTime(d.tanks) && isTime(d.kits) && (d.condition == null || (Number.isFinite(d.condition) && d.condition >= 0 && d.condition <= 100));
}

export function isValidUpdate(body) {
  if (!body || typeof body !== 'object') return false;
  const subs = body.subs ?? [];
  const voyages = body.voyages ?? [];
  if (!Array.isArray(subs) || subs.length > 16 || !Array.isArray(voyages) || voyages.length > 50) return false;
  if (!subs.every((s) => s && isSlot(s.slot) && isName(s.name) && isTime(s.returnTime) && isAge(s.age)
    && (s.details == null || isDetails(s.details)))) return false;
  if (!voyages.every(isVoyage)) return false;
  const active = body.activeBuffs;
  if (active != null && !(isAge(active.age) && Array.isArray(active.buffs) && active.buffs.length <= 16
    && active.buffs.every((b) => b && isName(b.name) && (b.seconds === null || isTime(b.seconds))))) return false;
  const plots = body.plots ?? [];
  const isPlot = (p) => p && isAge(p.age) && isTime(p.nextWaterAt) && (p.dieAt == null || isTime(p.dieAt))
    && (p.indoor == null || typeof p.indoor === 'boolean');
  if (!Array.isArray(plots) || plots.length > MAX_PLOTS) return false;
  if (!plots.every((p) => isPlot(p) && isName(p.key) && isName(p.name))) return false;
  if (body.garden != null && !isPlot(body.garden)) return false;
  const supplies = body.supplies;
  if (supplies != null && !(isAge(supplies.age) && isTime(supplies.tanks) && isTime(supplies.kits))) return false;
  if (body.buffs == null) return true;
  return isAge(body.buffs.age) && Array.isArray(body.buffs.slots) && body.buffs.slots.length <= 16
    && body.buffs.slots.every((b) => b && isSlot(b.slot) && isTime(b.itemId) && isName(b.name) && isTime(b.hours));
}

// Aplica uma atualização ao estado. Vale a leitura feita por último, medida no relógio do servidor.
export function applyUpdate(state, body, now) {
  for (const slot of Object.keys(state.subs)) {
    if (!isRealSlot(slot)) delete state.subs[slot];
  }

  for (const sub of (body.subs ?? []).filter((s) => isRealSlot(s.slot))) {
    const observedAt = now - sub.age;
    const existing = state.subs[sub.slot];
    if (existing && existing.observedAt > observedAt) continue;
    // Viagem vista pela primeira vez quando já tinha voltado não gera aviso atrasado.
    const same = existing?.returnTime === sub.returnTime;
    const notified = same ? existing.notified : (sub.returnTime <= now ? sub.returnTime : 0);
    const reminded = same ? existing.reminded ?? 0 : 0;
    // Plugin antigo não manda os detalhes: ficam os últimos conhecidos.
    const details = sub.details ?? existing?.details ?? null;
    const repairWarned = (existing?.repairWarned ?? false) && needsRepair({ details });
    state.subs[sub.slot] = { name: sub.name, returnTime: sub.returnTime, observedAt, notified, reminded, details, repairWarned };
  }

  // A rega de cada plot vale a partir do horário em que foi regado, então a mais nova sempre vence.
  // O formato com uma rega só vira o plot "Plantação".
  // Até a 0.8.2 a posição na chave seguia o idioma do PC (vírgula ou ponto). A chave fica sempre com ponto,
  // e as duas versões do mesmo canteiro viram uma, ficando a rega mais recente.
  state.plots ??= {};
  for (const [key, plot] of Object.entries(state.plots).filter(([k]) => k.includes(','))) {
    delete state.plots[key];
    const fixed = plotKey(key);
    if (!state.plots[fixed] || state.plots[fixed].observedAt < plot.observedAt) state.plots[fixed] = plot;
  }
  const plotUpdates = (body.plots ?? []).map((p) => ({ ...p, key: plotKey(p.key) }));
  if (body.garden) plotUpdates.push({ ...body.garden, key: 'geral', name: 'Plantação' });
  for (const update of plotUpdates) {
    const observedAt = now - update.age;
    const existing = state.plots[update.key];
    if (existing && observedAt < existing.observedAt) continue;
    const { nextWaterAt } = update;
    const dieAt = update.dieAt ?? 0;
    const same = existing?.nextWaterAt === nextWaterAt;
    state.plots[update.key] = {
      name: update.name,
      nextWaterAt,
      dieAt,
      observedAt,
      notified: same ? existing.notified : (nextWaterAt <= now ? nextWaterAt : 0),
      reminded: same ? existing.reminded : 0,
      warned: existing?.dieAt === dieAt ? existing.warned ?? 0 : 0,
      forgotten: same ? existing.forgotten ?? 0 : 0,
      indoor: update.indoor ?? existing?.indoor ?? false,
    };
  }

  // O estoque vale a partir de quando a contagem mudou, então a mais nova sempre vence.
  if (body.supplies) {
    const observedAt = now - body.supplies.age;
    if (!state.supplies || observedAt >= state.supplies.observedAt) {
      const { tanks, kits } = body.supplies;
      const supplies = { tanks, kits, observedAt, lowWarned: false };
      supplies.lowWarned = isLow(supplies, state) && (state.supplies?.lowWarned ?? false);
      state.supplies = supplies;
    }
  }

  if (body.buffs) {
    const observedAt = now - body.buffs.age;
    if (observedAt >= state.buffsObservedAt) {
      const buffs = {};
      for (const slot of body.buffs.slots) {
        const existing = state.buffs[slot.slot];
        // A mesa só informa horas inteiras, então o horário só muda quando esse número muda.
        const unchanged = existing && existing.itemId === slot.itemId && existing.hours === slot.hours;
        const readyTime = unchanged ? existing.readyTime : observedAt + (slot.hours * 3600);
        // Buff visto pela primeira vez já pronto não gera aviso atrasado.
        const notified = existing?.readyTime === readyTime ? existing.notified ?? 0 : (readyTime <= now ? readyTime : 0);
        buffs[slot.slot] = { itemId: slot.itemId, name: slot.name, hours: slot.hours, readyTime, notified };
      }
      state.buffs = buffs;
      state.buffsObservedAt = observedAt;
    }
  }

  if (body.activeBuffs) {
    const observedAt = now - body.activeBuffs.age;
    if (!state.activeBuffs || observedAt >= state.activeBuffs.observedAt) {
      const previous = state.activeBuffs?.buffs ?? [];
      const buffs = body.activeBuffs.buffs.map((buff) => {
        const endTime = buff.seconds === null ? null : observedAt + buff.seconds;
        const known = previous.find((p) => p.name === buff.name);
        // Pequenas diferenças entre leituras não mudam o horário nem geram edição.
        const steady = known?.endTime && endTime && Math.abs(known.endTime - endTime) <= END_TIME_TOLERANCE;
        // Quem não tem o tempo não apaga o horário que outro membro já leu.
        const keep = known?.endTime && endTime === null && known.endTime > observedAt;
        return { name: buff.name, endTime: steady || keep ? known.endTime : endTime };
      });
      state.activeBuffs = { buffs, observedAt };
    }
  }

  state.updatedAt = now;
  return state;
}

// A mesma viagem vira um único registro. Só é substituída por uma leitura com pelo menos tantos itens.
export function mergeVoyage(existing, incoming) {
  const items = incoming.items.map((i) => ({ itemId: i.itemId, name: i.name, count: i.count, value: i.value, ...(i.market ? { market: true } : {}) }));
  const voyage = {
    slot: incoming.slot,
    subName: incoming.subName,
    returnTime: incoming.returnTime,
    items,
    ...(incoming.world ? { world: incoming.world } : {}),
    ...(incoming.unlocked?.length ? { unlocked: incoming.unlocked } : {}),
  };
  if (!existing) return voyage;
  if (incoming.items.length < existing.items.length) return null;
  const same = (a, b) => a.length === b.length && a.every((x, n) => x.itemId === b[n].itemId && x.count === b[n].count && x.value === b[n].value);
  // A mesma viagem reenviada mantém os preços que o serviço já buscou.
  if (same(existing.items, items)) {
    const gained = items.some((i, n) => i.market && !existing.items[n].market) || (voyage.world && !existing.world);
    if (!gained) return null;
    return { ...existing, items: existing.items.map((i, n) => ({ ...i, ...(items[n].market ? { market: true } : {}) })), world: existing.world ?? voyage.world };
  }
  return voyage;
}

// Vale o maior entre o NPC e o market.
export const bestValue = (item) => Math.max(item.value, item.marketValue ?? 0);

// Viagem com item de market ainda sem preço.
export const needsPrice = (voyage) => Boolean(voyage.world) && voyage.items.some((i) => i.market && i.marketValue == null);

// Preço por unidade de cada item, a partir da resposta do Universalis. Usa a média das vendas recentes.
export function parseUniversalis(json) {
  const entries = json?.items ? Object.values(json.items) : json?.itemID ? [json] : [];
  const prices = {};
  for (const entry of entries) {
    const price = entry.averagePriceNQ || entry.averagePrice || entry.minPrice || 0;
    if (entry.itemID && price > 0) prices[entry.itemID] = price;
  }
  return prices;
}

// Coloca o valor de market nos itens e devolve o aviso, se algum item de market passar do limite.
export function priceVoyage(voyage, prices) {
  const items = voyage.items.map((i) => (i.market && i.marketValue == null && prices[i.itemId] != null
    ? { ...i, marketValue: Math.round(prices[i.itemId] * i.count) }
    : i));
  const priced = { ...voyage, items };
  const rich = items.filter((i) => i.marketValue != null && i.marketValue > i.value && i.marketValue >= MARKET_ALERT);
  const notice = rich.length === 0 ? null : {
    id: `market:${voyage.slot}:${voyage.returnTime}`, kind: 'marketDrop', subName: voyage.subName, world: voyage.world, items: rich,
  };
  return { voyage: priced, notice };
}

export function sectorNotice(voyage) {
  if (!voyage.unlocked?.length) return null;
  return { id: `sector:${voyage.slot}:${voyage.returnTime}`, kind: 'sectorUnlocked', subName: voyage.subName, sectors: voyage.unlocked };
}

function subLine(sub, now) {
  const rank = sub.details ? ` (R${sub.details.rank})` : '';
  const condition = sub.details?.condition;
  const wear = condition != null && condition < REPAIR_SHOW ? ` · ${needsRepair(sub) ? '⚠️' : '🔧'} ${percent(condition)}` : '';
  const name = `**${sub.name}**${rank}`;
  if (sub.returnTime === 0) return `${name}: parado${wear}`;
  if (sub.returnTime <= now) return `${name}: ✅ pronto${wear}`;
  return `${name}: volta <t:${sub.returnTime}:R> (<t:${sub.returnTime}:t>)${wear}`;
}

// Build e rota numa linha discreta no topo. Se os subs forem diferentes, cada um mostra a sua.
function setupLines(subs) {
  const setups = subs.filter((s) => s.details?.build).map((s) => `${s.details.build} · ${s.details.route}`);
  if (setups.length === 0) return [];
  if (new Set(setups).size === 1) return [`*${setups[0]}*`];
  return subs.filter((s) => s.details?.build).map((s) => `*${s.name}: ${s.details.build} · ${s.details.route}*`);
}

function buffLine(buff, now) {
  return buff.readyTime <= now ? `**${buff.name}**: ✅ pronto` : `**${buff.name}**: <t:${buff.readyTime}:R>`;
}

const bySlot = (record) => Object.keys(record).map(Number).sort((a, b) => a - b).map((slot) => record[slot]);
// Tira os ícones do jogo (caracteres de uso privado), que o Discord mostra como "▯".
const cleanName = (name) => name.replace(/[\uE000-\uF8FF]/g, '').trim() || 'Canteiro';
const shorten = (raw) => {
  const name = cleanName(raw);
  return name.length > MAX_NAME_SHOWN ? `${name.slice(0, MAX_NAME_SHOWN - 1)}…` : name;
};

function plotText(plot, now) {
  if (plot.nextWaterAt > now) return `regar <t:${plot.nextWaterAt}:R> (<t:${plot.nextWaterAt}:t>)`;
  if (plot.dieAt > now) return `✅ Pronta para regar. Morre <t:${plot.dieAt}:R>`;
  if (plot.dieAt > 0) return '⚠️ Passou do prazo de rega';
  return '✅ Pronta para regar.';
}

// Cada plot tem a sua linha, com o próprio horário.
function renderPlots(plots, now) {
  if (plots.length === 0) return 'Nenhum dado ainda.';
  const lines = plots.map((plot) => `**${shorten(plot.name)}**: ${plotText(plot, now)}`);
  if (lines.length > MAX_PLOT_LINES) return [...lines.slice(0, MAX_PLOT_LINES), `e mais ${lines.length - MAX_PLOT_LINES}`].join('\n');
  return lines.join('\n');
}

// Quantas rodadas dos subs os tanques aguentam, e quantos reparos completos os kits pagam.
function suppliesText(supplies, subs = [], lowTanks = LOW_TANKS) {
  const mark = (value, low) => (value < low ? `⚠️ **${gilFormat.format(value)}**` : `**${gilFormat.format(value)}**`);
  const known = subs.filter((s) => s.details);
  const tanksPerRound = known.reduce((sum, s) => sum + s.details.tanks, 0);
  const kitsPerRepair = Math.max(0, ...known.map((s) => s.details.kits));
  const rounds = tanksPerRound > 0 ? ` (≈ ${plural(Math.floor(supplies.tanks / tanksPerRound), 'rodada', 'rodadas')})` : '';
  const repairs = kitsPerRepair > 0 ? ` (≈ ${plural(Math.floor(supplies.kits / kitsPerRepair), 'reparo completo', 'reparos completos')})` : '';
  return [
    `${mark(supplies.tanks, lowTanks)} tanques de ceruleum${rounds}`,
    `${mark(supplies.kits, LOW_KITS)} kits de reparo${repairs}`,
    `Contado <t:${supplies.observedAt}:R>`,
  ].join('\n');
}

// O painel é sempre montado a partir do estado salvo e de todas as viagens do dia.
export function renderPanel(state, todayVoyages, now, showItems = SHOW_ITEMS) {
  const [, month, day] = dayOf(now).split('-');
  const total = todayVoyages.reduce((sum, v) => sum + v.items.reduce((s, i) => s + bestValue(i), 0), 0);
  const market = todayVoyages.reduce((sum, v) => sum + v.items.reduce((s, i) => s + (bestValue(i) > i.value ? bestValue(i) : 0), 0), 0);

  const perSub = new Map();
  const perItem = new Map();
  for (const voyage of todayVoyages) {
    const sub = perSub.get(voyage.slot) ?? { name: voyage.subName, value: 0 };
    sub.name = voyage.subName;
    for (const item of voyage.items) {
      sub.value += bestValue(item);
      const entry = perItem.get(item.itemId) ?? { name: item.name, count: 0, value: 0 };
      entry.count += item.count;
      entry.value += bestValue(item);
      perItem.set(item.itemId, entry);
    }
    perSub.set(voyage.slot, sub);
  }

  const subLoot = [...perSub.entries()].sort((a, b) => a[0] - b[0])
    .map(([, s]) => `**${shorten(s.name)}**: ${gilFormat.format(s.value)} gil`);
  const items = [...perItem.values()].sort((a, b) => b.value - a.value);
  const itemLines = items.slice(0, MAX_ITEMS_SHOWN).map((i) => `${i.count}x ${shorten(i.name)}: ${gilFormat.format(i.value)} gil`);
  if (items.length > MAX_ITEMS_SHOWN) itemLines.push(`e mais ${items.length - MAX_ITEMS_SHOWN} itens`);
  if (itemLines.length === 0) itemLines.push('Nenhum item ainda.');

  const active = state.activeBuffs;
  const activeLines = [];
  if (!active) activeLines.push('Nenhum dado ainda.');
  else {
    // Um buff com tempo sai sozinho do painel quando acaba, mesmo com todos offline.
    const buffs = active.buffs.filter((b) => !b.endTime || b.endTime > now);
    activeLines.push(buffs.length === 0 ? 'Nenhum' : buffs
      .map((b) => `**${shorten(b.name)}**: ${b.endTime ? `<t:${b.endTime}:R>` : '?'}`)
      .join('\n'));
    if (buffs.length === 0 || buffs.some((b) => !b.endTime)) activeLines.push(`Lido <t:${active.observedAt}:R>`);
  }
  const dalamud = dalamudLine(state.dalamud);
  if (dalamud || state.updatedAt) activeLines.push('');
  if (dalamud) activeLines.push(dalamud);
  if (state.updatedAt) activeLines.push(`||Atualizado <t:${state.updatedAt}:R>||`);

  const subList = bySlot(state.subs);
  const subs = [...setupLines(subList), ...subList.map((s) => subLine(s, now))];
  const buffs = bySlot(state.buffs).map((b) => buffLine(b, now));
  const plotLines = renderPlots(activePlots(state, now), now);

  return {
    allowed_mentions: { parse: [] },
    embeds: [{
      title: 'Timers',
      color: 0x3B82F6,
      fields: [
        { name: '🚢 Submarinos', value: subs.join('\n') || 'Nenhum dado ainda.' },
        { name: '⚙️ Buffs', value: buffs.join('\n') || 'Nenhum dado ainda.' },
        ...(state.supplies ? [{ name: '⛽ Estoque', value: suppliesText(state.supplies, subList, lowTanksOf(state)) }] : []),
        { name: '🌱 Plantação', value: plotLines },
        { name: `💰 Loot de hoje (${day}/${month})`, value: [
          `**${market > 0 ? '≈ ' : ''}${gilFormat.format(total)} gil**${market > 0 ? ` (inclui 🏷️ ${gilFormat.format(market)} de market)` : ''}`,
          ...subLoot,
        ].join('\n') },
        ...(showItems ? [{ name: '📦 Itens de hoje', value: itemLines.join('\n') }] : []),
        { name: '✨ Buffs ativos', value: activeLines.join('\n') },
      ],
    }],
  };
}

export function nextMidnight(now) {
  const today = dayOf(now);
  let time = (Math.floor(now / DAY_STEP) + 1) * DAY_STEP;
  while (dayOf(time) === today) time += DAY_STEP;
  return time;
}

// Próximo momento em que o painel muda ou um aviso sai, sem ninguém enviar nada.
export function nextEvent(state, now) {
  const plots = Object.values(state.plots ?? {});
  const timed = [...Object.values(state.subs).map((s) => s.returnTime), ...plots.map((p) => p.nextWaterAt)].filter((t) => t > 0);
  const times = [
    ...timed,
    ...timed.map((t) => t - REMINDER_LEAD),
    ...plots.filter((p) => p.dieAt > 0).flatMap((p) => [p.dieAt, ...DEATH_WARNINGS.map((before) => p.dieAt - before)]),
    ...Object.values(state.subs).filter((s) => s.returnTime > 0).map((s) => s.returnTime + FORGOTTEN_SUB),
    ...plots.filter((p) => p.indoor).map((p) => p.observedAt + FORGOTTEN_PLOT),
    ...Object.values(state.buffs).map((b) => b.readyTime),
    ...(state.activeBuffs?.buffs ?? []).map((b) => b.endTime ?? 0),
    ...(state.alerts ?? []).map((a) => a.deleteAt),
    nextMidnight(now),
    ...(state.dalamud ? [state.dalamud.checkedAt + DALAMUD_CHECK] : []),
  ];
  return Math.min(...times.filter((t) => t > now));
}

// O que o plugin recebe em GET /state: o estado sem os dados internos do Discord.
export function publicState(state, voyages, now) {
  return {
    now,
    subs: state.subs,
    buffs: state.buffs,
    buffsObservedAt: state.buffsObservedAt,
    activeBuffs: state.activeBuffs,
    plots: state.plots ?? {},
    supplies: state.supplies ? { tanks: state.supplies.tanks, kits: state.supplies.kits, observedAt: state.supplies.observedAt } : null,
    voyages,
  };
}

// Os últimos dias, do mais antigo para hoje, no fuso do painel.
export function recentDays(now, count = HISTORY_DAYS) {
  const days = new Set();
  for (let t = now; days.size < count; t -= DAY_STEP) days.add(dayOf(t));
  return [...days].reverse();
}
