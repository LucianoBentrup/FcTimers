// O /fcc: um comando só, em inglês, que abre um menu que só quem chamou vê. Cada botão troca o conteúdo da
// mesma mensagem. Os comandos antigos em português (/subs, /loot...) viraram seções deste menu.
//
// Formato do custom_id dos botões, pensado para crescer sem quebrar botões já postados:
//   fc:<seção>[:<arg>]       abre uma seção (ex.: fc:loot:week)
//   fc:do:<ação>             ação de quem gerencia o servidor (ex.: fc:do:repost)
//   fc:share:<seção>[:<arg>] posta a seção no canal, para todo mundo ver
// Reservado para a 0.17: fc:ev:<id do evento>:<escolha> nos botões de inscrição dos posts de evento.

import { EPHEMERAL } from './discord.js';

export const MESSAGE_COMPONENT = 3;
export const UPDATE_MESSAGE = 7;
export const DEFERRED_UPDATE_MESSAGE = 6;

const BUTTON = 2;
const ROW = 1;
const PRIMARY = 1;
const SECONDARY = 2;
const SUCCESS = 3;
const DANGER = 4;
const MAX_DESCRIPTION = 4000;
const COLOR = 0x3B82F6;

export const LANGS = ['en', 'pt'];
// A FC atual fala português. Na 0.13 a FC nova entra com o idioma escolhido na configuração (inglês por padrão).
export const DEFAULT_LANG = 'pt';
export const langOf = (state) => (LANGS.includes(state?.settings?.lang) ? state.settings.lang : DEFAULT_LANG);

// Registrado com tools/registrar-comandos.ps1. O nome é /fcc em qualquer idioma.
export const FC_COMMAND = {
  name: 'fcc',
  description: 'FC Companion: panel, subs, loot, supplies and server settings',
  description_localizations: { 'pt-BR': 'FC Companion: painel, subs, loot, estoque e ajustes do servidor' },
  contexts: [0],
};
// O que o tools/registrar-comandos.ps1 envia (tools/comandos.json, gerado por `npm run commands`).
export const COMMANDS = [FC_COMMAND];

export const TEXT = {
  en: {
    panel: 'Panel', subs: 'Subs', loot: 'Loot', supplies: 'Supplies', profit: 'Profit', manage: 'Manage', share: 'Post in channel',
    day: 'Today', week: '7 days', month: '30 days',
    subsTitle: '🚢 Submarines', suppliesTitle: '⛽ Supplies', profitTitle: '💰 Profit', lootTitle: '💰 Loot', manageTitle: '⚙️ Server settings',
    footer: 'Only you can see this menu.',
    sharedBy: (user) => `Posted by <@${user}> with /fcc`,
    repost: 'Repost panel', regroup: 'Group alerts', weeklyOn: 'Weekly summary: on', weeklyOff: 'Weekly summary: off', log: 'Plugin log',
    language: 'Menu: English',
    manageText: [
      '**Repost panel**: deletes the panel and posts it again at the bottom of the channel.',
      '**Group alerts**: replaces repeated alerts with grouped ones, without pinging.',
      '**Weekly summary**: posted every Monday at 9:00. Click to turn it on or off.',
      '**Plugin log**: the latest diagnostic log, as a file only you see.',
      '**Menu**: language of this menu for the whole server.',
    ].join('\n'),
    panelReposted: '✅ Panel reposted at the bottom of the channel.',
    regroupWorking: '⏳ Grouping alerts…',
    regroupDone: (removed, sent) => `✅ ${removed} alerts deleted, ${sent} grouped alerts posted.`,
    regroupFailed: '⚠️ Could not group the alerts right now. Try again in a few minutes.',
    weeklyNowOn: '✅ Weekly summary on: every Monday at 9:00.',
    weeklyNowOff: '✅ Weekly summary off.',
    langNow: '✅ Menu in English.',
    noLog: 'No log uploaded yet.',
    logOf: (name, at) => `Log from **${name}**, uploaded <t:${at}:R>.`,
    notLinked: 'This server is not linked to an FC in FC Companion yet.',
    managersOnly: 'Only server managers can use this.',
    unknown: 'Unknown command.',
    moved: 'This command moved into **/fcc**.',
  },
  pt: {
    panel: 'Painel', subs: 'Subs', loot: 'Loot', supplies: 'Estoque', profit: 'Lucro', manage: 'Ajustes', share: 'Postar no canal',
    day: 'Hoje', week: '7 dias', month: '30 dias',
    subsTitle: '🚢 Submarinos', suppliesTitle: '⛽ Estoque', profitTitle: '💰 Lucro', lootTitle: '💰 Loot', manageTitle: '⚙️ Ajustes do servidor',
    footer: 'Só você vê este menu.',
    sharedBy: (user) => `Postado por <@${user}> pelo /fcc`,
    repost: 'Reenviar painel', regroup: 'Agrupar avisos', weeklyOn: 'Resumo semanal: ligado', weeklyOff: 'Resumo semanal: desligado', log: 'Log do plugin',
    language: 'Menu: Português',
    manageText: [
      '**Reenviar painel**: apaga o painel e manda outro no fim do canal.',
      '**Agrupar avisos**: troca avisos repetidos por avisos agrupados, sem notificar.',
      '**Resumo semanal**: sai toda segunda às 9h. Clique para ligar ou desligar.',
      '**Log do plugin**: o último log de diagnóstico, como arquivo que só você vê.',
      '**Menu**: idioma deste menu para o servidor todo.',
    ].join('\n'),
    panelReposted: '✅ Painel reenviado no fim do canal.',
    regroupWorking: '⏳ Agrupando os avisos…',
    regroupDone: (removed, sent) => `✅ ${removed} avisos apagados, ${sent} agrupados enviados.`,
    regroupFailed: '⚠️ Não consegui agrupar os avisos agora. Tente de novo em alguns minutos.',
    weeklyNowOn: '✅ Resumo semanal ligado: sai toda segunda às 9h.',
    weeklyNowOff: '✅ Resumo semanal desligado.',
    langNow: '✅ Menu em português.',
    noLog: 'Nenhum log enviado ainda.',
    logOf: (name, at) => `Log de **${name}**, enviado <t:${at}:R>.`,
    notLinked: 'Este servidor ainda não está ligado a uma FC no FC Companion.',
    managersOnly: 'Só quem gerencia o servidor pode usar isto.',
    unknown: 'Comando desconhecido.',
    moved: 'Este comando agora fica dentro do **/fcc**.',
  },
};

export const text = (lang, key) => (TEXT[lang] ?? TEXT.en)[key] ?? TEXT.en[key];

// Seções do menu, na ordem dos botões. A 0.17 entra aqui com { id: 'events' }.
export const SECTIONS = [
  { id: 'panel', emoji: '📋' },
  { id: 'subs', emoji: '🚢', share: true },
  { id: 'loot', emoji: '💰', share: true, args: ['day', 'week', 'month'] },
  { id: 'supplies', emoji: '⛽', share: true },
  { id: 'profit', emoji: '📈', share: true },
];
const MANAGE = { id: 'manage', emoji: '⚙️', manager: true };
const ACTIONS = ['repost', 'regroup', 'weekly-on', 'weekly-off', 'log', 'lang-en', 'lang-pt'];
const sectionOf = (id) => [...SECTIONS, MANAGE].find((s) => s.id === id);

// Os comandos antigos abrem a seção equivalente do /fcc enquanto o Discord ainda mostrar o comando.
export const LEGACY = {
  painel: { view: 'manage' }, avisos: { view: 'manage' }, resumo: { view: 'manage' }, log: { view: 'manage' },
  subs: { view: 'subs' }, estoque: { view: 'supplies' }, lucro: { view: 'profit' },
  loot: { view: 'loot', args: { dia: 'day', semana: 'week', mes: 'month' } },
};

// Lê um custom_id do menu. Devolve null para qualquer coisa que não seja do /fcc ou que não exista mais.
export function parseCustomId(id) {
  if (typeof id !== 'string' || id.length > 100) return null;
  const [prefix, kind, name, extra] = id.split(':');
  if (prefix !== 'fc') return null;
  if (kind === 'do') return ACTIONS.includes(name) ? { kind: 'action', name } : null;
  const share = kind === 'share';
  const view = share ? name : kind;
  const arg = share ? extra : name;
  const section = sectionOf(view);
  if (!section || (share && !section.share)) return null;
  const validArg = section.args ? (section.args.includes(arg) ? arg : section.args[0]) : undefined;
  return { kind: share ? 'share' : 'view', view, arg: validArg, manager: Boolean(section.manager) };
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

// Botões do menu: a linha das seções, as opções da seção aberta e, para quem gerencia, os ajustes.
export function menuComponents(lang, { view, arg, manager, weeklyOff }) {
  const t = (key) => text(lang, key);
  const rows = [row(SECTIONS.map((s) => button(t(s.id), `fc:${s.id}`, s.id === view ? PRIMARY : SECONDARY, s.emoji)))];
  const section = sectionOf(view);

  if (view === 'loot') {
    rows.push(row(section.args.map((a) => button(t(a), `fc:loot:${a}`, a === arg ? PRIMARY : SECONDARY))));
  }
  if (view === 'manage') {
    rows.push(row([
      button(t('repost'), 'fc:do:repost', SECONDARY, '📋'),
      button(t('regroup'), 'fc:do:regroup', SECONDARY, '🔔'),
      weeklyOff ? button(t('weeklyOff'), 'fc:do:weekly-on', SECONDARY, '🗓️') : button(t('weeklyOn'), 'fc:do:weekly-off', SUCCESS, '🗓️'),
      button(t('log'), 'fc:do:log', SECONDARY, '🧾'),
      button(t('language'), lang === 'en' ? 'fc:do:lang-pt' : 'fc:do:lang-en', SECONDARY, '🌐'),
    ]));
  }

  const extra = [];
  if (section?.share) extra.push(button(t('share'), `fc:share:${view}${arg ? `:${arg}` : ''}`, SUCCESS, '📣'));
  if (manager) extra.push(button(t('manage'), 'fc:manage', view === 'manage' ? PRIMARY : DANGER, MANAGE.emoji));
  if (extra.length > 0) rows.push(row(extra));
  return rows;
}

// A mensagem inteira do menu. `notice` é uma linha no topo depois de uma ação ("Painel reenviado").
export function menuMessage(lang, { embed, view, arg, manager, weeklyOff, notice }) {
  return {
    content: notice ?? '',
    embeds: [{ ...embed, footer: { text: text(lang, 'footer') } }],
    components: menuComponents(lang, { view, arg, manager, weeklyOff }),
    flags: EPHEMERAL,
    allowed_mentions: { parse: [] },
  };
}

// A seção postada no canal: sem botões e sem marcar ninguém.
export function sharedMessage(lang, embed, userId) {
  return {
    content: userId ? text(lang, 'sharedBy')(userId) : '',
    embeds: [embed],
    allowed_mentions: { parse: [] },
  };
}

// Qual FC atende este servidor. Hoje é uma só, ligada por DISCORD_GUILD_ID. Na 0.13 vira uma busca do servidor
// no registro de FCs (cada FC com o seu Durable Object), e esta é a única função que muda.
export function fcFor(interaction, env) {
  if (!interaction.guild_id) return null;
  if (env.DISCORD_GUILD_ID && interaction.guild_id !== env.DISCORD_GUILD_ID) return null;
  return 'fc';
}

export const userOf = (interaction) => interaction.member?.user?.id ?? interaction.user?.id ?? null;
