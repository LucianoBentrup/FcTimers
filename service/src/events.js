// Eventos da FC: tipos, inscrição com reserva, lembretes, repetição semanal e o post no canal.
// Só funções puras. O objeto da FC (index.js) guarda os eventos e fala com o Discord.

import { TIME_ZONE } from './logic.js';
import { suggestedJob } from './link.js';
import { text } from './texts.js';

const BUTTON = 2;
const ROW = 1;
const PRIMARY = 1;
const SECONDARY = 2;
const SUCCESS = 3;
const DANGER = 4;
const COLOR = 0x8A6FE0;
const CLOSED_COLOR = 0x4A5666;

// Três tipos. O tipo decide os botões de inscrição e o nome do campo livre.
export const EVENT_TYPES = {
  group: { roles: ['tank', 'healer', 'dps'], backup: true, field: 'fDuty' },
  gathering: { roles: ['going', 'maybe'], backup: false, field: 'fLocation' },
  other: { roles: ['going', 'maybe'], backup: false, field: 'fWhat' },
};
export const SIZES = {
  4: { tank: 1, healer: 1, dps: 2 },
  8: { tank: 2, healer: 2, dps: 4 },
  24: { tank: 3, healer: 6, dps: 15 },
};
export const LEADS = { '1h': [3600], '15m': [900], both: [3600, 900] };
export const NOTIFY = ['channel', 'dm', 'both'];
const ROLE_EMOJI = { tank: '🛡️', healer: '💚', dps: '⚔️', going: '✅', maybe: '❔', backup: '🔁', cant: '✖️' };

// O evento fecha 2 horas depois do início: os botões saem e, se repete, o próximo é postado.
export const CLOSE_AFTER = 2 * 3600;
// Evento fechado fica guardado 14 dias, depois sai.
export const KEEP_CLOSED = 14 * 86400;
export const MAX_OPEN = 25;
// Nomes por função mostrados no post. O resto vira "+N".
const MAX_NAMES = 30;
const MAX_TITLE = 60;
const MAX_FIELD = 80;
const MAX_DESC = 400;

export const isType = (type) => Object.hasOwn(EVENT_TYPES, type);
export const choicesOf = (type) => [...EVENT_TYPES[type].roles, ...(EVENT_TYPES[type].backup ? ['backup'] : []), 'cant'];
export const slotsOf = (event, role) => (event.type === 'group' ? SIZES[event.size]?.[role] ?? 0 : 0);
export const capacityOf = (event) => (event.type === 'group' ? Object.values(SIZES[event.size]).reduce((a, b) => a + b, 0) : 0);

// ---------- data e hora ----------

const zoneFormat = (tz) => new Intl.DateTimeFormat('en-US', {
  timeZone: tz, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', weekday: 'short',
});
function partsIn(tz, ms) {
  const parts = Object.fromEntries(zoneFormat(tz).formatToParts(new Date(ms)).map((p) => [p.type, p.value]));
  return {
    year: Number(parts.year), month: Number(parts.month), day: Number(parts.day), hour: Number(parts.hour), minute: Number(parts.minute),
    weekday: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].indexOf(parts.weekday),
  };
}

// Hora local num fuso para unix. Duas passadas acertam a diferença do fuso, inclusive em horário de verão.
export function zonedToUnix(year, month, day, hour, minute, tz = TIME_ZONE) {
  const wanted = Date.UTC(year, month - 1, day, hour, minute);
  let guess = wanted;
  for (let n = 0; n < 2; n += 1) {
    const p = partsIn(tz, guess);
    guess += wanted - Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute);
  }
  return Math.floor(guess / 1000);
}

const WEEKDAYS = {
  sun: 0, dom: 0, mon: 1, seg: 1, tue: 2, ter: 2, wed: 3, qua: 3, thu: 4, qui: 4, fri: 5, sex: 5, sat: 6, sab: 6,
};
const plain = (value) => value.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');

// Lê "ter 21:00", "tomorrow 20:30", "hoje 9pm", "02/12 21:00", "2026-12-02 21h". No fuso da FC.
// Devolve { start } ou { error: 'badWhen' | 'pastWhen' }.
export function parseWhen(input, now, tz = TIME_ZONE) {
  const value = plain(String(input ?? '')).trim();
  const time = value.match(/(?:^|\s)(\d{1,2})(?::(\d{2}))?\s*(h|am|pm)?\s*$/);
  if (!time) return { error: 'badWhen' };
  let hour = Number(time[1]);
  const minute = Number(time[2] ?? 0);
  if (time[3] === 'pm' && hour < 12) hour += 12;
  if (time[3] === 'am' && hour === 12) hour = 0;
  if (!time[2] && !time[3]) return { error: 'badWhen' };
  if (hour > 23 || minute > 59) return { error: 'badWhen' };

  const day = value.slice(0, time.index).trim();
  const today = partsIn(tz, now * 1000);
  // Data que não existe (32/12, 30/02) vira NaN.
  const at = (y, m, d) => {
    const unix = zonedToUnix(y, m, d, hour, minute, tz);
    const p = partsIn(tz, unix * 1000);
    return p.year === y && p.month === m && p.day === d ? unix : NaN;
  };
  const shift = (days) => {
    const p = partsIn(tz, (zonedToUnix(today.year, today.month, today.day, 12, 0, tz) + (days * 86400)) * 1000);
    return at(p.year, p.month, p.day);
  };

  let start;
  let explicit = false;
  let match;
  if (day === '' || day === 'today' || day === 'hoje') {
    start = shift(0);
    if (start <= now && day === '') start = shift(1);
  } else if (day === 'tomorrow' || day === 'amanha') {
    start = shift(1);
  } else if ((match = day.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/))) {
    start = at(Number(match[1]), Number(match[2]), Number(match[3]));
    explicit = true;
  } else if ((match = day.match(/^(\d{1,2})\/(\d{1,2})(?:\/(\d{2,4}))?$/))) {
    const year = match[3] ? Number(match[3].length === 2 ? `20${match[3]}` : match[3]) : today.year;
    start = at(year, Number(match[2]), Number(match[1]));
    if (!match[3] && start <= now) start = at(year + 1, Number(match[2]), Number(match[1]));
    explicit = true;
  } else if (Object.hasOwn(WEEKDAYS, day.slice(0, 3)) && /^[a-z]+$/.test(day)) {
    let ahead = (WEEKDAYS[day.slice(0, 3)] - today.weekday + 7) % 7;
    if (ahead === 0 && shift(0) <= now) ahead = 7;
    start = shift(ahead);
  } else {
    return { error: 'badWhen' };
  }
  if (!Number.isFinite(start)) return { error: 'badWhen' };
  if (start <= now) return { error: explicit || day === 'today' || day === 'hoje' ? 'pastWhen' : 'badWhen' };
  return { start };
}

// ---------- rascunho e criação ----------

export const newDraft = (type, user) => ({
  type, size: 8, title: '', when: '', field: '', desc: '', start: null, error: null, repeat: false, notify: 'channel', lead: '1h', by: user,
});

const clip = (value, max) => String(value ?? '').replace(/\s+/g, ' ').trim().slice(0, max);

// Os campos do formulário entram no rascunho. A data é lida de novo a cada vez.
export function applyFields(draft, fields, now, tz = TIME_ZONE) {
  draft.title = clip(fields.title, MAX_TITLE);
  draft.when = clip(fields.when, 40);
  draft.field = clip(fields.field, MAX_FIELD);
  draft.desc = String(fields.desc ?? '').trim().slice(0, MAX_DESC);
  const parsed = draft.when ? parseWhen(draft.when, now, tz) : { error: 'badWhen' };
  draft.start = parsed.start ?? null;
  draft.error = !draft.title ? 'noTitle' : parsed.error ?? null;
  return draft;
}

// Mudanças dos botões da revisão. Devolve false para uma mudança que não existe.
export function changeDraft(draft, op, value) {
  if (op === 'size' && draft.type === 'group' && SIZES[value]) draft.size = Number(value);
  else if (op === 'repeat') draft.repeat = !draft.repeat;
  else if (op === 'notify') draft.notify = NOTIFY[(NOTIFY.indexOf(draft.notify) + 1) % NOTIFY.length];
  else if (op === 'lead') {
    const keys = Object.keys(LEADS);
    draft.lead = keys[(keys.indexOf(draft.lead) + 1) % keys.length];
  } else return false;
  return true;
}

const emptyRoles = (type) => Object.fromEntries(EVENT_TYPES[type].roles.map((r) => [r, []]));

export function eventFromDraft(draft, id, now) {
  return {
    id, series: id, type: draft.type, size: draft.type === 'group' ? draft.size : null,
    title: draft.title, field: draft.field, desc: draft.desc, start: draft.start,
    repeat: Boolean(draft.repeat), notify: draft.notify, lead: draft.lead, by: draft.by,
    roles: emptyRoles(draft.type), reserve: [], cant: [], sent: [],
    status: 'open', createdAt: now, closedAt: null, guildId: null, channelId: null, messageId: null,
  };
}

// A mesma semana seguinte, com a lista limpa. Pula semanas se o serviço ficou parado.
export function nextOccurrence(event, id, now) {
  let start = event.start + (7 * 86400);
  while (start <= now) start += 7 * 86400;
  return {
    ...event, id, start, roles: emptyRoles(event.type), reserve: [], cant: [], sent: [],
    status: 'open', createdAt: now, closedAt: null, messageId: null,
  };
}

// ---------- inscrição ----------

// Onde a pessoa está: { where: 'role' | 'reserve' | 'cant', role } ou null.
export function placeOf(event, user) {
  for (const [role, list] of Object.entries(event.roles)) if (list.includes(user)) return { where: 'role', role };
  const reserve = event.reserve.find((r) => r.user === user);
  if (reserve) return { where: 'reserve', role: reserve.role };
  if (event.cant.includes(user)) return { where: 'cant', role: 'cant' };
  return null;
}

function remove(event, user) {
  const place = placeOf(event, user);
  for (const role of Object.keys(event.roles)) event.roles[role] = event.roles[role].filter((u) => u !== user);
  event.reserve = event.reserve.filter((r) => r.user !== user);
  event.cant = event.cant.filter((u) => u !== user);
  return place;
}

// Quem está na reserva sobe, na ordem em que entrou, quando alguém sai de uma função cheia. Sobe quem pediu essa
// função ou quem clicou em "Reserva" (qualquer função). Vaga que já estava livre não puxa ninguém da reserva.
function promote(event, role) {
  const promoted = [];
  const slots = role ? slotsOf(event, role) : 0;
  while (slots && event.roles[role].length < slots) {
    const index = event.reserve.findIndex((r) => r.role === role || r.role === 'any');
    if (index < 0) break;
    const [entry] = event.reserve.splice(index, 1);
    event.roles[role].push(entry.user);
    promoted.push({ user: entry.user, role });
  }
  return promoted;
}

// Um clique num botão do post. Clicar de novo onde já está tira a pessoa.
// Devolve { outcome: 'joined' | 'reserve' | 'left' | 'cant' | 'closed', role, position, promoted }.
export function signUp(event, user, choice) {
  if (event.status !== 'open') return { outcome: 'closed', promoted: [] };
  if (!choicesOf(event.type).includes(choice)) return { outcome: 'closed', promoted: [] };
  const before = remove(event, user);
  const same = before && (
    (before.where === 'role' && before.role === choice)
    || (before.where === 'reserve' && (before.role === choice || (before.role === 'any' && choice === 'backup')))
    || (before.where === 'cant' && choice === 'cant'));

  let result;
  if (same) result = { outcome: 'left', role: before.role };
  else if (choice === 'cant') {
    event.cant.push(user);
    result = { outcome: 'cant', role: 'cant' };
  } else if (choice === 'backup') {
    event.reserve.push({ user, role: 'any' });
    result = { outcome: 'reserve', role: null, position: event.reserve.length };
  } else {
    const slots = slotsOf(event, choice);
    if (slots && event.roles[choice].length >= slots) {
      event.reserve.push({ user, role: choice });
      result = { outcome: 'reserve', role: choice, position: event.reserve.length };
    } else {
      event.roles[choice].push(user);
      result = { outcome: 'joined', role: choice };
    }
  }
  return { ...result, promoted: promote(event, before?.where === 'role' ? before.role : null) };
}

// Quem recebe lembrete: os inscritos nas funções, incluindo Talvez. Reserva e Não vou ficam de fora.
export const attendees = (event) => [...new Set(Object.values(event.roles).flat())];

// ---------- tempo dos eventos ----------

export const dueReminders = (event, now) => (event.status === 'open'
  ? LEADS[event.lead].filter((lead) => !event.sent.includes(lead) && now >= event.start - lead && now < event.start)
  : []);
export const closeDue = (event, now) => event.status === 'open' && now >= event.start + CLOSE_AFTER;
export const pruneDue = (event, now) => event.status !== 'open' && now - (event.closedAt ?? event.start) >= KEEP_CLOSED;

// Próximo momento em que algum evento precisa de algo (lembrete, fechar ou sair da lista). Infinity se nenhum.
export function eventsWakeAt(events, now) {
  const times = [];
  for (const event of Object.values(events)) {
    if (event.status === 'open') {
      for (const lead of LEADS[event.lead]) if (!event.sent.includes(lead)) times.push(event.start - lead);
      times.push(event.start + CLOSE_AFTER);
    } else {
      times.push((event.closedAt ?? event.start) + KEEP_CLOSED);
    }
  }
  const future = times.filter((t) => t > now);
  return future.length > 0 ? Math.min(...future) : Infinity;
}

export const openEvents = (events) => Object.values(events).filter((e) => e.status === 'open').sort((a, b) => a.start - b.start);

// ---------- o post ----------

const button = (label, customId, style = SECONDARY, emoji = null, disabled = false) => ({
  type: BUTTON, style, label, custom_id: customId, ...(emoji ? { emoji: { name: emoji } } : {}), ...(disabled ? { disabled: true } : {}),
});
const row = (components) => ({ type: ROW, components });

function names(list, state, role) {
  const shown = list.slice(0, MAX_NAMES).map((u) => {
    const job = role ? suggestedJob(state, u, role) : null;
    return `<@${u}>${job ? ` ${job}` : ''}`;
  });
  if (list.length > MAX_NAMES) shown.push(`+${list.length - MAX_NAMES}`);
  return shown;
}

// O embed do evento, igual no post, na revisão e na lista.
export function eventEmbed(event, lang, state = {}) {
  const t = (key) => text(lang, key);
  const roleName = t('role');
  const closed = event.status !== 'open';
  const lines = [`<t:${event.start}:F> · <t:${event.start}:R>${event.repeat ? ` · ${t('everyWeek')}` : ''}`];
  if (event.field) lines.push(`**${t(EVENT_TYPES[event.type].field)}:** ${event.field}`);
  if (event.desc) lines.push('', event.desc);

  const fields = Object.entries(event.roles).map(([role, list]) => {
    const slots = slotsOf(event, role);
    const open = Math.max(0, slots - list.length);
    const value = [...names(list, state, event.type === 'group' ? role : null), ...Array(Math.min(open, 4)).fill(`*${t('open')}*`)];
    return {
      name: `${ROLE_EMOJI[role]} ${roleName[role]} ${list.length}${slots ? `/${slots}` : ''}`,
      value: value.join('\n') || `*${t('nobody')}*`,
      inline: true,
    };
  });
  if (event.reserve.length > 0) {
    fields.push({
      name: `${ROLE_EMOJI.backup} ${t('reserve')}`,
      value: event.reserve.slice(0, MAX_NAMES).map((r, n) => `${n + 1}. <@${r.user}>${r.role !== 'any' ? ` (${roleName[r.role]})` : ''}`).join('\n'),
      inline: false,
    });
  }
  if (event.cant.length > 0) fields.push({ name: `${ROLE_EMOJI.cant} ${roleName.cant}`, value: names(event.cant, state).join(', '), inline: false });

  const kind = event.type === 'group' ? `${t('group')} · ${t('size')(event.size)}` : t(event.type);
  const filled = event.type === 'group' ? ` · ${t('spots')(attendees(event).length, capacityOf(event))}` : '';
  const status = closed ? ` (${t(event.status === 'cancelled' ? 'cancelled' : 'ended')})` : '';
  return {
    title: `${event.status === 'cancelled' ? '~~' : ''}${event.title}${event.status === 'cancelled' ? '~~' : ''}${status}`,
    description: lines.join('\n'),
    color: closed ? CLOSED_COLOR : COLOR,
    fields,
    footer: { text: `${kind}${filled}\n${t('remindLine')(event.lead, event.notify)}` },
  };
}

// O post no canal: o embed, os botões de inscrição e, numa segunda linha, cancelar.
export function eventPost(event, lang, state = {}) {
  const t = (key) => text(lang, key);
  const roleName = t('role');
  const createdBy = event.by ? `${t('createdBy')(event.by)}` : '';
  const message = { content: createdBy, embeds: [eventEmbed(event, lang, state)], allowed_mentions: { parse: [] }, components: [] };
  if (event.status !== 'open') return message;
  message.components = [
    row(choicesOf(event.type).map((choice) => button(roleName[choice], `fc:ev:${event.id}:${choice}`,
      choice === 'cant' ? SECONDARY : choice === 'backup' ? SECONDARY : PRIMARY, ROLE_EMOJI[choice]))),
    row([button(t('cancelEvent'), `fc:evx:${event.id}:cancel`, DANGER)]),
  ];
  return message;
}

// Linha do evento na seção Eventos do /fcc, com a situação de quem abriu.
export function eventLine(event, lang, user) {
  const t = (key) => text(lang, key);
  const place = placeOf(event, user);
  let mine = t('notAnswered');
  if (place?.where === 'role') mine = t('role')[place.role];
  if (place?.where === 'reserve') mine = `${t('reserve')} #${event.reserve.findIndex((r) => r.user === user) + 1}`;
  if (place?.where === 'cant') mine = t('role').cant;
  const link = event.messageId && event.guildId ? ` · https://discord.com/channels/${event.guildId}/${event.channelId}/${event.messageId}` : '';
  return `**${event.title}** · <t:${event.start}:f> · ${mine}${link}`;
}

// ---------- telas do /fcc ----------

export function typePicker(lang) {
  const t = (key) => text(lang, key);
  return {
    embed: {
      title: `${t('eventsTitle')} · ${t('newEvent')}`,
      description: [t('pickType'), '', ...Object.keys(EVENT_TYPES).map((type) => `**${t(type)}**: ${t(`${type}Hint`)}`)].join('\n'),
      color: COLOR,
    },
    components: [row([
      button(t('group'), 'fc:evnew:group', PRIMARY, ROLE_EMOJI.dps),
      button(t('gathering'), 'fc:evnew:gathering', PRIMARY, '📍'),
      button(t('other'), 'fc:evnew:other', PRIMARY, '⭐'),
      button(t('back'), 'fc:events'),
    ])],
  };
}

const input = (id, label, value, { style = 1, max, required = true, placeholder } = {}) => ({
  type: ROW,
  components: [{
    type: 4, custom_id: id, label, style, required, max_length: max,
    ...(value ? { value } : {}), ...(placeholder ? { placeholder } : {}),
  }],
});

// O formulário do evento (resposta tipo MODAL). Volta preenchido ao editar.
export function eventModal(draft, lang) {
  const t = (key) => text(lang, key);
  return {
    custom_id: `fc:evm:${draft.type}`,
    title: t('modalTitle')(t(draft.type)).slice(0, 45),
    components: [
      input('title', t('fTitle'), draft.title, { max: MAX_TITLE }),
      input('when', t('fWhen'), draft.when, { max: 40, placeholder: t('fWhenHint') }),
      input('field', t(EVENT_TYPES[draft.type].field), draft.field, { max: MAX_FIELD, required: false }),
      input('desc', t('fDesc'), draft.desc, { style: 2, max: MAX_DESC, required: false }),
    ],
  };
}

// Campos do formulário enviado. Procura em qualquer nível, porque o Discord pode embrulhar os campos.
export function modalFields(data) {
  const out = {};
  const walk = (list) => {
    for (const item of list ?? []) {
      if (item.custom_id && typeof item.value === 'string') out[item.custom_id] = item.value;
      walk(item.components);
      if (item.component) walk([item.component]);
    }
  };
  walk(data?.components);
  return out;
}

// A revisão antes de postar: o post como vai ficar e os ajustes em botões.
export function draftReview(draft, lang, channelId) {
  const t = (key) => text(lang, key);
  const preview = { ...eventFromDraft(draft, 'draft', 0), start: draft.start ?? 0 };
  const embeds = [];
  const problem = draft.error ? t(draft.error) : null;
  if (draft.start) embeds.push(eventEmbed(preview, lang));
  const rows = [];
  if (draft.type === 'group') {
    rows.push(row(Object.keys(SIZES).map((n) => button(t('size')(n), `fc:evd:size:${n}`, Number(n) === draft.size ? PRIMARY : SECONDARY))));
  }
  rows.push(row([
    button(draft.repeat ? t('repeatWeekly') : t('repeatNo'), 'fc:evd:repeat', draft.repeat ? PRIMARY : SECONDARY, '🔁'),
    button(t('notify')[draft.notify], 'fc:evd:notify', SECONDARY, '🔔'),
    button(t('lead')[draft.lead], 'fc:evd:lead', SECONDARY, '⏰'),
  ]));
  rows.push(row([
    button(t('post'), 'fc:evd:post', SUCCESS, null, Boolean(problem)),
    button(t('edit'), 'fc:evd:edit'),
    button(t('cancel'), 'fc:evd:cancel', DANGER),
  ]));
  return {
    content: problem ? `⚠️ ${problem}` : t('reviewText')(channelId),
    embeds,
    components: rows,
  };
}

// O que o plugin recebe em GET /state: os próximos eventos, sem quem se inscreveu (a ligação ainda não existe).
export const eventsForPlugin = (events) => openEvents(events).map((e) => ({
  id: e.id, type: e.type, title: e.title, field: e.field, start: e.start, repeat: e.repeat,
  signedUp: attendees(e).length, capacity: capacityOf(e),
}));
