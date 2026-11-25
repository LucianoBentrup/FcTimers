// O /fcc: um comando só, em inglês, que abre um menu no canal, à vista de todos. Qualquer pessoa clica nas abas e
// a mesma mensagem muda pra todo mundo. O que é de uma pessoa só (Ajustes, novo evento, lembrete por DM, o log)
// abre numa mensagem que só ela vê. Os comandos antigos em português (/subs, /loot...) viraram seções deste menu.
//
// Os formatos de custom_id estão em parseCustomId, mais abaixo.

import { EPHEMERAL } from './discord.js';
import { isType } from './events.js';
import { text } from './texts.js';

export { DEFAULT_LANG, LANGS, langOf, text, TEXT } from './texts.js';

export const MESSAGE_COMPONENT = 3;
export const UPDATE_MESSAGE = 7;
export const DEFERRED_UPDATE_MESSAGE = 6;
export const MODAL = 9;
export const MODAL_SUBMIT = 5;

const BUTTON = 2;
const ROLE_SELECT = 6;
const ROW = 1;
const PRIMARY = 1;
const SECONDARY = 2;
const SUCCESS = 3;
const DANGER = 4;
const LINK = 5;
const MAX_DESCRIPTION = 4000;
const COLOR = 0x3B82F6;

// Link do Ko-fi do projeto. Único lugar do serviço com a URL: com ela preenchida, aparece um botão "Ko-fi" em
// todas as telas do /fcc e embaixo do painel do bot. Vazia, o botão não aparece.
export const SUPPORT_URL = '';

// Registrado com tools/registrar-comandos.ps1. O nome é /fcc em qualquer idioma.
export const FC_COMMAND = {
  name: 'fcc',
  description: 'FC Companion: panel, subs, loot, supplies, events and server settings',
  description_localizations: { 'pt-BR': 'FC Companion: painel, subs, loot, estoque, eventos e ajustes do servidor' },
  contexts: [0],
};
// O que o tools/registrar-comandos.ps1 envia (tools/comandos.json, gerado por `npm run commands`).
export const COMMANDS = [FC_COMMAND];

// Seções do menu, na ordem dos botões. As 5 primeiras ficam na primeira linha.
export const SECTIONS = [
  { id: 'panel', emoji: '📋' },
  { id: 'subs', emoji: '🚢', share: true },
  { id: 'loot', emoji: '💰', share: true, args: ['day', 'week', 'month'] },
  { id: 'supplies', emoji: '⛽', share: true },
  { id: 'profit', emoji: '📈', share: true },
  { id: 'events', emoji: '📅' },
];
const MANAGE = { id: 'manage', emoji: '⚙️', manager: true };
export const ACTIONS = ['repost', 'regroup', 'weekly-on', 'weekly-off', 'log', 'lang-en', 'lang-pt', 'panel-here', 'alerts-here', 'events-here'];
const DRAFT_OPS = ['size', 'repeat', 'notify', 'lead', 'post', 'edit', 'cancel'];
const sectionOf = (id) => [...SECTIONS, MANAGE].find((s) => s.id === id);

// Os comandos antigos abrem a seção equivalente do /fcc enquanto o Discord ainda mostrar o comando.
export const LEGACY = {
  painel: { view: 'manage' }, avisos: { view: 'manage' }, resumo: { view: 'manage' }, log: { view: 'manage' },
  subs: { view: 'subs' }, estoque: { view: 'supplies' }, lucro: { view: 'profit' },
  loot: { view: 'loot', args: { dia: 'day', semana: 'week', mes: 'month' } },
};

const ID = /^[a-z0-9]{1,16}$/;

// Lê um custom_id. Devolve null para qualquer coisa que não seja do /fcc ou que não exista mais.
//   fc:<seção>[:<arg>]          troca a seção do menu (ex.: fc:loot:week)
//   fc:open:<seção>[:<arg>]     abre o menu já na seção, numa mensagem nova no canal (botões do painel)
//   fc:share:<seção>[:<arg>]    posta a seção no canal
//   fc:do:<ação>                ajuste de quem gerencia o servidor
//   fc:set:eventrole            lista de cargos dos ajustes
//   fc:pref:dm                  lembrete de evento por DM, de cada um
//   fc:evnew[:<tipo>]           novo evento: escolher o tipo, depois o formulário
//   fc:evm:<tipo>               formulário do evento enviado
//   fc:evd:<op>[:<valor>]       botões da revisão do rascunho
//   fc:ev:<id>:<escolha>        inscrição no post do evento
//   fc:evx:<id>:cancel          cancelar o evento
export function parseCustomId(id) {
  if (typeof id !== 'string' || id.length > 100) return null;
  const [prefix, kind, name, extra, ...rest] = id.split(':');
  if (prefix !== 'fc' || rest.length > 0) return null;
  switch (kind) {
    case 'do': return ACTIONS.includes(name) && extra === undefined ? { kind: 'action', name } : null;
    case 'set': return name === 'eventrole' ? { kind: 'eventrole' } : null;
    case 'pref': return name === 'dm' ? { kind: 'pref', name } : null;
    case 'evnew': return name === undefined || isType(name) ? { kind: 'evnew', type: name ?? null } : null;
    case 'evm': return isType(name) ? { kind: 'evmodal', type: name } : null;
    case 'evd': return DRAFT_OPS.includes(name) ? { kind: 'evdraft', op: name, value: extra ?? null } : null;
    case 'ev': return ID.test(name ?? '') && ['tank', 'healer', 'dps', 'going', 'maybe', 'backup', 'cant'].includes(extra)
      ? { kind: 'signup', id: name, choice: extra } : null;
    case 'evx': return ID.test(name ?? '') && extra === 'cancel' ? { kind: 'evcancel', id: name } : null;
    default: break;
  }
  const moved = kind === 'share' || kind === 'open';
  const view = moved ? name : kind;
  const arg = moved ? extra : name;
  const section = sectionOf(view);
  if (!section || (kind === 'share' && !section.share)) return null;
  const validArg = section.args ? (section.args.includes(arg) ? arg : section.args[0]) : undefined;
  return { kind: moved ? kind : 'view', view, arg: validArg, manager: Boolean(section.manager) };
}

const button = (label, customId, style = SECONDARY, emoji = null) => ({
  type: BUTTON, style, label, custom_id: customId, ...(emoji ? { emoji: { name: emoji } } : {}),
});
const row = (components) => ({ type: ROW, components });

function truncate(value) {
  return value.length > MAX_DESCRIPTION ? `${value.slice(0, MAX_DESCRIPTION - 1)}…` : value;
}

// Embed de uma seção a partir do texto que o painel já sabe montar.
export const sectionEmbed = (title, body) => ({ title, description: truncate(body || '—'), color: COLOR });

// Botões do menu. Linha 1: as 5 primeiras seções. Depois, as opções da seção aberta. Por último: Eventos,
// Postar no canal e, para quem gerencia, Ajustes.
// Em `events`: canCreate e dmOn. Em `manage`: weeklyOff e eventRole.
// `pub`: o menu no canal. Ele é igual pra todo mundo, então não depende de quem clicou por último: Novo evento e
// Ajustes aparecem sempre (a permissão é conferida no clique), o botão de DM não mostra o estado de ninguém e não
// tem Postar no canal, porque o menu já está no canal.
export function menuComponents(lang, {
  view, arg, manager, weeklyOff, canCreate, dmOn, eventRole, pub = false, supportUrl = SUPPORT_URL,
}) {
  const t = (key) => text(lang, key);
  const tab = (s) => button(t(s.id), `fc:${s.id}`, s.id === view ? PRIMARY : SECONDARY, s.emoji);
  const rows = [row(SECTIONS.slice(0, 5).map(tab))];
  const section = sectionOf(view);

  if (view === 'loot') {
    rows.push(row(section.args.map((a) => button(t(a), `fc:loot:${a}`, a === arg ? PRIMARY : SECONDARY))));
  }
  if (view === 'events') {
    const options = [];
    if (canCreate || pub) options.push(button(t('newEvent'), 'fc:evnew', SUCCESS, '➕'));
    if (pub) options.push(button(t('dmToggle'), 'fc:pref:dm', SECONDARY, '✉️'));
    else options.push(button(dmOn ? t('dmOn') : t('dmOff'), 'fc:pref:dm', dmOn ? PRIMARY : SECONDARY, '✉️'));
    rows.push(row(options));
  }
  if (view === 'manage') {
    rows.push(row([
      button(t('repost'), 'fc:do:repost', SECONDARY, '📋'),
      button(t('regroup'), 'fc:do:regroup', SECONDARY, '🔔'),
      weeklyOff ? button(t('weeklyOff'), 'fc:do:weekly-on', SECONDARY, '🗓️') : button(t('weeklyOn'), 'fc:do:weekly-off', SUCCESS, '🗓️'),
      button(t('log'), 'fc:do:log', SECONDARY, '🧾'),
      button(t('language'), lang === 'en' ? 'fc:do:lang-pt' : 'fc:do:lang-en', SECONDARY, '🌐'),
    ]));
    rows.push(row([
      button(t('panelHere'), 'fc:do:panel-here', SECONDARY, '📋'),
      button(t('alertsHere'), 'fc:do:alerts-here', SECONDARY, '🔔'),
      button(t('eventsHere'), 'fc:do:events-here', SECONDARY, '📅'),
    ]));
    rows.push(row([{
      type: ROLE_SELECT, custom_id: 'fc:set:eventrole', placeholder: t('eventRole'), min_values: 0, max_values: 1,
      ...(eventRole ? { default_values: [{ id: eventRole, type: 'role' }] } : {}),
    }]));
  }

  const last = SECTIONS.slice(5).map(tab);
  if (section?.share && !pub) last.push(button(t('share'), `fc:share:${view}${arg ? `:${arg}` : ''}`, SUCCESS, '📣'));
  if (pub) last.push(button(t('manage'), 'fc:manage', SECONDARY, MANAGE.emoji));
  else if (manager) last.push(button(t('manage'), 'fc:manage', view === 'manage' ? PRIMARY : DANGER, MANAGE.emoji));
  // Botão de link: abre o navegador, não chega ao serviço.
  if (supportUrl) last.push(supportButton(lang, supportUrl));
  rows.push(row(last));
  return rows;
}

// A mensagem inteira do menu. `notice` é uma linha no topo depois de uma ação ("Painel reenviado").
// `pub`: no canal, à vista de todos. Sem ele, só quem abriu vê (Ajustes e as telas de evento).
export function menuMessage(lang, { embed, notice, pub = false, ...options }) {
  return {
    content: notice ?? '',
    embeds: [{ ...embed, footer: { text: text(lang, pub ? 'footerPublic' : 'footer') } }],
    components: menuComponents(lang, { ...options, pub }),
    ...(pub ? {} : { flags: EPHEMERAL }),
    allowed_mentions: { parse: [] },
  };
}

// Uma tela própria dentro do menu (escolher o tipo do evento, revisar o rascunho). Sem as abas.
export const screenMessage = ({ content = '', embeds = [], components = [] }) => ({
  content, embeds, components, flags: EPHEMERAL, allowed_mentions: { parse: [] },
});

// A seção postada no canal: sem botões e sem marcar ninguém.
export function sharedMessage(lang, embed, userId) {
  return {
    content: userId ? text(lang, 'sharedBy')(userId) : '',
    embeds: [embed],
    allowed_mentions: { parse: [] },
  };
}

// Botão de link pro Ko-fi: abre o navegador e não chega ao serviço.
export const supportButton = (lang, url = SUPPORT_URL) => ({ type: BUTTON, style: LINK, label: text(lang, 'support'), url, emoji: { name: '☕' } });

// Botões embaixo do painel postado pelo bot. Cada um abre o /fcc já na seção, no canal. O último é o Ko-fi.
export function panelComponents(lang, supportUrl = SUPPORT_URL) {
  const t = (key) => text(lang, key);
  return [row([
    button(t('todayLoot'), 'fc:open:loot:day', SECONDARY, '💰'),
    button(t('supplies'), 'fc:open:supplies', SECONDARY, '⛽'),
    button(t('events'), 'fc:open:events', SECONDARY, '📅'),
    ...(supportUrl ? [supportButton(lang, supportUrl)] : []),
  ])];
}

// Qual FC atende este servidor. Hoje é uma só, ligada por DISCORD_GUILD_ID. Na 0.13 vira uma busca do servidor
// no registro de FCs (cada FC com o seu Durable Object), e esta é a única função que muda.
export function fcFor(interaction, env) {
  if (!interaction.guild_id) return null;
  if (env.DISCORD_GUILD_ID && interaction.guild_id !== env.DISCORD_GUILD_ID) return null;
  return 'fc';
}

export const userOf = (interaction) => interaction.member?.user?.id ?? interaction.user?.id ?? null;
