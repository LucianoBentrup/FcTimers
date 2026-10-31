// Regras do painel, sem dependência do Cloudflare. Ver ../../REGRAS.md.
export const TIME_ZONE = 'America/Sao_Paulo';
const MAX_ITEMS_SHOWN = 8;
const MAX_NAME_SHOWN = 60;
const END_TIME_TOLERANCE = 120;
const ALERT_FALLBACK_SECONDS = 86400;
const DAY_STEP = 900;

const dayFormat = new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit' });
const gilFormat = new Intl.NumberFormat('pt-BR');

export const newState = () => ({
  subs: {}, buffs: {}, buffsObservedAt: 0, activeBuffs: null, alerts: [], updatedAt: 0, messageId: null, lastPanel: null,
});

// Submarinos que voltaram e ainda não geraram aviso.
export const pendingReturns = (state, now) => Object.values(state.subs)
  .filter((s) => s.returnTime > 0 && s.returnTime <= now && s.notified !== s.returnTime);

// O aviso dura até a volta do submarino mais demorado do momento.
// Sem nenhum em viagem, não há retorno para usar, e vale o limite de segurança.
export function alertDeleteAt(state, now) {
  const returns = Object.values(state.subs).map((s) => s.returnTime).filter((t) => t > now);
  return returns.length > 0 ? Math.max(...returns) : now + ALERT_FALLBACK_SECONDS;
}

export function alertPayload(subs, roleId) {
  const names = subs.map((s) => `**${s.name}**`);
  const list = names.length > 1 ? `${names.slice(0, -1).join(', ')} e ${names.at(-1)}` : names[0];
  const text = `🚢 ${list} ${names.length > 1 ? 'voltaram' : 'voltou'}!`;
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
  if (body.buffs == null) return true;
  return isAge(body.buffs.age) && Array.isArray(body.buffs.slots) && body.buffs.slots.length <= 16
    && body.buffs.slots.every((b) => b && isSlot(b.slot) && isTime(b.itemId) && isName(b.name) && isTime(b.hours));
}

// Aplica uma atualização ao estado. Vale a leitura feita por último, medida no relógio do servidor.
export function applyUpdate(state, body, now) {
  for (const sub of body.subs ?? []) {
    const observedAt = now - sub.age;
    const existing = state.subs[sub.slot];
    if (existing && existing.observedAt > observedAt) continue;
    // Viagem vista pela primeira vez quando já tinha voltado não gera aviso atrasado.
    const notified = existing?.returnTime === sub.returnTime
      ? existing.notified
      : (sub.returnTime <= now ? sub.returnTime : 0);
    state.subs[sub.slot] = { name: sub.name, returnTime: sub.returnTime, observedAt, notified };
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

// O painel é sempre montado a partir do estado salvo e de todas as viagens do dia.
export function renderPanel(state, todayVoyages, now) {
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

  return {
    allowed_mentions: { parse: [] },
    embeds: [{
      title: 'Timers',
      color: 0x3B82F6,
      fields: [
        { name: '🚢 Submarinos', value: subs.join('\n') || 'Nenhum dado ainda.' },
        { name: '⚙️ Buffs', value: buffs.join('\n') || 'Nenhum dado ainda.' },
        { name: `💰 Loot de hoje (${day}/${month})`, value: [`**${gilFormat.format(total)} gil**`, ...subLoot].join('\n') },
        { name: '📦 Itens de hoje', value: itemLines.join('\n') },
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

// Próximo momento em que o painel muda sem ninguém enviar nada.
export function nextEvent(state, now) {
  const times = [
    ...Object.values(state.subs).map((s) => s.returnTime),
    ...Object.values(state.buffs).map((b) => b.readyTime),
    ...(state.activeBuffs?.buffs ?? []).map((b) => b.endTime ?? 0),
    ...(state.alerts ?? []).map((a) => a.deleteAt),
    nextMidnight(now),
  ];
  return Math.min(...times.filter((t) => t > now));
}
