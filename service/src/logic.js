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

// Até a versão 0.6.0 o plugin enviava uma 5ª posição, que repete um dos 4 submarinos.
export const isRealSlot = (slot) => Number(slot) < MAX_SUBMARINES;

const dayFormat = new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit' });
const gilFormat = new Intl.NumberFormat('pt-BR');

export const newState = () => ({
  subs: {}, buffs: {}, buffsObservedAt: 0, activeBuffs: null, plots: {}, alerts: [], updatedAt: 0, messageId: null, lastPanel: null,
});

// O lembrete sai 1 hora antes. Se o serviço só puder enviar com menos de 30 minutos restando, ele não sai.
const reminderDue = (time, reminded, now) => reminded !== time && now >= time - REMINDER_LEAD && now < time - REMINDER_MIN_LEFT;

// Avisos que precisam sair agora, já agrupados. Cada submarino e cada rega avisa uma vez só.
export function pendingAlerts(state, now) {
  const subs = Object.values(state.subs).filter((s) => s.returnTime > 0);
  const alerts = [];

  const ready = subs.filter((s) => s.returnTime <= now && s.notified !== s.returnTime);
  if (ready.length > 0) alerts.push({ kind: 'subReady', subs: ready });

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

  return alerts;
}

// Plots com horário de rega, sem os que morreram há muito tempo ou foram retirados.
export function activePlots(state, now) {
  return Object.entries(state.plots ?? {})
    .map(([key, plot]) => ({ key, ...plot }))
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
    const sub = Object.values(state.subs).find((s) => s.name === sent.name && s.returnTime === sent.returnTime);
    if (!sub) continue;
    if (alert.kind === 'subReady') sub.notified = sub.returnTime;
    else sub.reminded = sub.returnTime;
  }
  for (const sent of alert.plots ?? []) {
    const plot = state.plots?.[sent.key];
    if (!plot) continue;
    if (alert.kind === 'plotReady' && plot.nextWaterAt === sent.nextWaterAt) plot.notified = plot.nextWaterAt;
    if (alert.kind === 'plotReminder' && plot.nextWaterAt === sent.nextWaterAt) plot.reminded = plot.nextWaterAt;
    if (alert.kind === 'plotDying' && plot.dieAt === sent.dieAt) plot.warned = sent.step;
  }
}

export const alertDeleteAt = (sentAt) => sentAt + ALERT_LIFETIME;

const joinList = (names) => (names.length > 1 ? `${names.slice(0, -1).join(', ')} e ${names.at(-1)}` : names[0]);
const joinNames = (list) => joinList(list.map((s) => `**${s.name}**`));

export function alertPayload(alert, roleId) {
  const texts = {
    subReady: () => `🚢 ${joinNames(alert.subs)} ${alert.subs.length > 1 ? 'voltaram' : 'voltou'}!`,
    subReminder: () => `Lembrete: Em 1 hora os submarinos estarão prontos para coleta (${joinNames(alert.subs)}).`,
    plotReady: () => `🌱 A plantação já pode ser regada (${joinNames(alert.plots)})!`,
    plotReminder: () => `Lembrete: Em 1 hora a plantação estará disponível para regar (${joinNames(alert.plots)}).`,
    plotDying: () => `⚠️ Se ninguém regar, ${alert.plots.length > 1 ? 'morrem' : 'morre'}: ${joinList(alert.plots.map((p) => `**${p.name}** <t:${p.dieAt}:R>`))}!`,
  };
  const text = texts[alert.kind]();
  return {
    content: roleId ? `<@&${roleId}> ${text}` : text,
    allowed_mentions: { parse: [], roles: roleId ? [roleId] : [] },
  };
}

export const dayOf = (unix) => dayFormat.format(new Date(unix * 1000));

export const voyageKey = (voyage) => `voyage:${dayOf(voyage.returnTime)}:${voyage.slot}:${voyage.returnTime}`;

const isSlot = (v) => Number.isInteger(v) && v >= 0 && v < 32;
const isTime = (v) => Number.isInteger(v) && v >= 0;
const isName = (v) => typeof v === 'string' && v.length > 0 && v.length <= 128;
const isAge = (v) => Number.isFinite(v) && v >= 0;

function isVoyage(v) {
  return v && isSlot(v.slot) && isName(v.subName) && isTime(v.returnTime) && v.returnTime > 0
    && Array.isArray(v.items) && v.items.length > 0 && v.items.length <= 16
    && v.items.every((i) => i && isTime(i.itemId) && isName(i.name) && isTime(i.count) && isTime(i.value));
}

export function isValidUpdate(body) {
  if (!body || typeof body !== 'object') return false;
  const subs = body.subs ?? [];
  const voyages = body.voyages ?? [];
  if (!Array.isArray(subs) || subs.length > 16 || !Array.isArray(voyages) || voyages.length > 50) return false;
  if (!subs.every((s) => s && isSlot(s.slot) && isName(s.name) && isTime(s.returnTime) && isAge(s.age))) return false;
  if (!voyages.every(isVoyage)) return false;
  const active = body.activeBuffs;
  if (active != null && !(isAge(active.age) && Array.isArray(active.buffs) && active.buffs.length <= 16
    && active.buffs.every((b) => b && isName(b.name) && (b.seconds === null || isTime(b.seconds))))) return false;
  const plots = body.plots ?? [];
  const isPlot = (p) => p && isAge(p.age) && isTime(p.nextWaterAt) && (p.dieAt == null || isTime(p.dieAt));
  if (!Array.isArray(plots) || plots.length > MAX_PLOTS) return false;
  if (!plots.every((p) => isPlot(p) && isName(p.key) && isName(p.name))) return false;
  if (body.garden != null && !isPlot(body.garden)) return false;
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
    state.subs[sub.slot] = { name: sub.name, returnTime: sub.returnTime, observedAt, notified, reminded };
  }

  // A rega de cada plot vale a partir do horário em que foi regado, então a mais nova sempre vence.
  // O formato com uma rega só vira o plot "Plantação".
  const plotUpdates = [...(body.plots ?? [])];
  if (body.garden) plotUpdates.push({ ...body.garden, key: 'geral', name: 'Plantação' });
  state.plots ??= {};
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
    };
  }

  if (body.buffs) {
    const observedAt = now - body.buffs.age;
    if (observedAt >= state.buffsObservedAt) {
      const buffs = {};
      for (const slot of body.buffs.slots) {
        const existing = state.buffs[slot.slot];
        // A mesa só informa horas inteiras, então o horário só muda quando esse número muda.
        const unchanged = existing && existing.itemId === slot.itemId && existing.hours === slot.hours;
        buffs[slot.slot] = {
          itemId: slot.itemId,
          name: slot.name,
          hours: slot.hours,
          readyTime: unchanged ? existing.readyTime : observedAt + (slot.hours * 3600),
        };
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
  const voyage = { slot: incoming.slot, subName: incoming.subName, returnTime: incoming.returnTime, items: incoming.items };
  if (!existing) return voyage;
  if (incoming.items.length < existing.items.length) return null;
  return JSON.stringify(existing.items) === JSON.stringify(incoming.items) ? null : voyage;
}

function subLine(sub, now) {
  if (sub.returnTime === 0) return `**${sub.name}**: parado`;
  if (sub.returnTime <= now) return `**${sub.name}**: ✅ pronto`;
  return `**${sub.name}**: volta <t:${sub.returnTime}:R> (<t:${sub.returnTime}:t>)`;
}

function buffLine(buff, now) {
  return buff.readyTime <= now ? `**${buff.name}**: ✅ pronto` : `**${buff.name}**: <t:${buff.readyTime}:R>`;
}

const bySlot = (record) => Object.keys(record).map(Number).sort((a, b) => a - b).map((slot) => record[slot]);
const shorten = (name) => (name.length > MAX_NAME_SHOWN ? `${name.slice(0, MAX_NAME_SHOWN - 1)}…` : name);

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

// O painel é sempre montado a partir do estado salvo e de todas as viagens do dia.
export function renderPanel(state, todayVoyages, now, showItems = SHOW_ITEMS) {
  const [, month, day] = dayOf(now).split('-');
  const total = todayVoyages.reduce((sum, v) => sum + v.items.reduce((s, i) => s + i.value, 0), 0);

  const perSub = new Map();
  const perItem = new Map();
  for (const voyage of todayVoyages) {
    const sub = perSub.get(voyage.slot) ?? { name: voyage.subName, value: 0 };
    sub.name = voyage.subName;
    for (const item of voyage.items) {
      sub.value += item.value;
      const entry = perItem.get(item.itemId) ?? { name: item.name, count: 0, value: 0 };
      entry.count += item.count;
      entry.value += item.value;
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
  if (state.updatedAt) activeLines.push('', `||Atualizado <t:${state.updatedAt}:R>||`);

  const subs = bySlot(state.subs).map((s) => subLine(s, now));
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
        { name: '🌱 Plantação', value: plotLines },
        { name: `💰 Loot de hoje (${day}/${month})`, value: [`**${gilFormat.format(total)} gil**`, ...subLoot].join('\n') },
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
    ...Object.values(state.buffs).map((b) => b.readyTime),
    ...(state.activeBuffs?.buffs ?? []).map((b) => b.endTime ?? 0),
    ...(state.alerts ?? []).map((a) => a.deleteAt),
    nextMidnight(now),
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
    voyages,
  };
}

// Os últimos dias, do mais antigo para hoje, no fuso do painel.
export function recentDays(now, count = HISTORY_DAYS) {
  const days = new Set();
  for (let t = now; days.size < count; t -= DAY_STEP) days.add(dayOf(t));
  return [...days].reverse();
}
