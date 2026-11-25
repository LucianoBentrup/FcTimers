import { DurableObject } from 'cloudflare:workers';
import {
  alertDeleteAt, alertPayload, applyUpdate, dayOf, isRealSlot, isValidUpdate, MARKET_CACHE, markSent, mergeVoyage, needsPrice, newState,
  applyDalamud, dalamudDue, nextEvent, parseUniversalis, pendingAlerts, priceVoyage, publicState, recentDays, recentSubAlerts, renderPanel, sectorNotice, voyageKey,
  profitReport, VOYAGE_KEEP_DAYS, weeklyDue, weeklySummary,
} from './logic.js';
import {
  canManage, COMMAND, EPHEMERAL, optionValue, PING, privateReply, REPLY, verifyDiscord,
} from './discord.js';
import { eventsForPlugin, modalFields } from './events.js';
import { InteractMethods } from './interact.js';
import {
  DEFERRED_UPDATE_MESSAGE, fcFor, LEGACY, MESSAGE_COMPONENT, MODAL_SUBMIT, panelComponents, parseCustomId, setupMessage, text, userOf,
} from './menu.js';
import { langOf } from './texts.js';
import { DISCORD_API, transportFor, transportOf } from './transport.js';
import { charactersOf } from './link.js';
import { LEGACY_FC } from './registry.js';

export { Registry } from './registry.js';

const DEBOUNCE_MS = 2000;
const RETRY_MS = 60000;
const MAX_BODY_BYTES = 256 * 1024;
const MAX_LOGS = 10;
const MAX_LOG_NAME = 40;
// Viagens de até 1 dia atrás ainda geram aviso de setor novo e de item de market.
const NOTICE_WINDOW = 86400;
const UNIVERSALIS = 'https://universalis.app/api/v2';

function addNotice(state, notice, returnTime, now) {
  if (!notice || now - returnTime > NOTICE_WINDOW) return;
  state.notices ??= [];
  if (!state.notices.some((n) => n.id === notice.id)) state.notices.push(notice);
}

const json = (body, status = 200) => Response.json(body, { status });
const nowSeconds = () => Math.floor(Date.now() / 1000);

function isAuthorized(request, env) {
  const expected = new TextEncoder().encode(`Bearer ${env.FC_KEY}`);
  const received = new TextEncoder().encode(request.headers.get('Authorization') ?? '');
  return env.FC_KEY && expected.byteLength === received.byteLength && crypto.subtle.timingSafeEqual(expected, received);
}

// Comandos, botões e formulários do Discord. A assinatura do app substitui a chave da FC nesta rota.
// Aqui só se confere quem é quem e o que foi clicado. O objeto da FC decide e monta a resposta.
async function handleInteraction(request, env, ctx) {
  const body = await request.text();
  const signed = await verifyDiscord(
    env.DISCORD_PUBLIC_KEY, request.headers.get('X-Signature-Ed25519'), request.headers.get('X-Signature-Timestamp'), body,
  );
  if (!signed) return new Response('invalid request signature', { status: 401 });

  const interaction = JSON.parse(body);
  if (interaction.type === PING) return json({ type: PING });

  const both = (key) => `${text('en', key)}\n${text('pt', key)}`;
  const manager = canManage(interaction);
  let asked;
  if (interaction.type === COMMAND) {
    const name = interaction.data?.name;
    const legacy = LEGACY[name];
    if (name === 'fcc') asked = { kind: 'command', view: 'panel' };
    else if (legacy) asked = { kind: 'command', view: legacy.view, arg: legacy.args?.[optionValue(interaction, 'periodo')], moved: true };
  } else if (interaction.type === MESSAGE_COMPONENT) {
    asked = parseCustomId(interaction.data?.custom_id);
    if (asked?.kind === 'evmodal') asked = null;
    if (asked?.kind === 'eventrole') asked.values = interaction.data?.values ?? [];
  } else if (interaction.type === MODAL_SUBMIT) {
    asked = parseCustomId(interaction.data?.custom_id);
    if (asked?.kind !== 'evmodal') asked = null;
    else asked.fields = modalFields(interaction.data);
  }
  if (!asked) return json(privateReply(text('en', 'unknown')));

  // Qual FC atende este servidor: o registro primeiro, depois a FC antiga do DISCORD_GUILD_ID.
  const registry = registryOf(env);
  let fc = interaction.guild_id ? (await ask(registry, '/guild', { guildId: interaction.guild_id }))?.fc : null;
  fc ??= fcFor(interaction, env);
  const codeAction = asked.kind === 'setup' || (asked.kind === 'action' && ['code', 'code-new', 'code-off'].includes(asked.name));
  if (!fc) {
    // Servidor novo: quem gerencia vê o botão de criar a FC; os outros, que ainda não está ligado.
    if (!manager || !interaction.guild_id) return json(privateReply(both('notLinked')));
    if (asked.kind !== 'setup') return json({ type: REPLY, data: setupMessage() });
  }
  if (codeAction && manager && interaction.guild_id) {
    const entry = await ask(registry, '/create', {
      guildId: interaction.guild_id, guildName: await guildName(env, interaction.guild_id), legacy: fc === LEGACY_FC,
    });
    fc = entry.fc;
    if (asked.kind === 'setup') asked = { kind: 'action', name: 'code' };
  }
  // A permissão é conferida a cada clique: o menu pode ter sido aberto antes de a pessoa perder o cargo.
  if ((asked.kind === 'action' || asked.kind === 'eventrole' || asked.kind === 'setup' || asked.manager) && !manager) return json(privateReply(both('managersOnly')));

  const payload = {
    ...asked,
    fc,
    user: userOf(interaction),
    manager,
    roles: interaction.member?.roles ?? [],
    guildId: interaction.guild_id,
    channelId: interaction.channel_id ?? interaction.channel?.id ?? null,
    // O clique veio do menu no canal (e não de uma mensagem que só a pessoa vê).
    pub: interaction.type !== COMMAND && Boolean(interaction.message) && !((interaction.message.flags ?? 0) & EPHEMERAL),
  };
  const stub = env.FC_STATE.get(env.FC_STATE.idFromName(fc));
  const interact = async () => (await stub.fetch('https://fc/interact', { method: 'POST', body: JSON.stringify(payload) })).json();
  const hook = `${env.DISCORD_API ?? DISCORD_API}/webhooks/${env.DISCORD_APP_ID}/${interaction.token}`;

  if (asked.kind === 'action' && asked.name === 'regroup') {
    // Apagar e reenviar avisos passa dos 3 segundos que o Discord espera: responde já e edita o menu depois.
    ctx.waitUntil((async () => {
      const { response } = await interact();
      const { flags, ...data } = response.data;
      await fetch(`${hook}/messages/@original`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) });
    })());
    return json({ type: DEFERRED_UPDATE_MESSAGE });
  }

  const out = await interact();
  // Um menu novo no canal toma o lugar do anterior do mesmo canal, pra não encher o canal de menus.
  const opened = (asked.kind === 'command' || asked.kind === 'open') && out.response?.type === REPLY && !(out.response.data?.flags & EPHEMERAL);
  if (opened && payload.channelId) ctx.waitUntil(replaceMenu(env, stub, hook, payload.channelId, interaction.token, Date.now()));
  // Uma segunda mensagem, só para quem clicou ("você está na reserva").
  if (out.followup) {
    ctx.waitUntil(fetch(hook, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(out.followup) }));
  }
  if (!out.file) return json(out.response);

  const form = new FormData();
  form.append('payload_json', JSON.stringify({
    ...out.response,
    data: { ...out.response.data, attachments: [{ id: 0, filename: out.file.name }] },
  }));
  form.append('files[0]', new Blob([out.file.text], { type: 'text/plain' }), out.file.name);
  return new Response(form);
}

const MENU_TOKEN_LIFE = 14 * 60;
const wait = (ms) => new Promise((resolve) => { setTimeout(resolve, ms); });

// Depois de responder ao /fcc: descobre o id do menu novo, guarda como o menu do canal e apaga o anterior.
// Sem o bot, o anterior só pode ser apagado pelo token dele, que vale 15 minutos. Depois disso, fica no canal.
// `openedAt` (ms) decide qual é o mais novo quando dois /fcc chegam juntos.
async function replaceMenu(env, stub, hook, channel, token, openedAt) {
  const api = env.DISCORD_API ?? DISCORD_API;
  let id = null;
  for (let tries = 0; tries < 3 && !id; tries += 1) {
    if (tries > 0) await wait(1000);
    try {
      const response = await fetch(`${hook}/messages/@original`);
      if (response.ok) id = (await response.json()).id ?? null;
    } catch {
      // Tenta de novo.
    }
  }
  if (!id) return;
  const now = nowSeconds();
  const previous = await (await stub.fetch('https://fc/menu', { method: 'POST', body: JSON.stringify({ channel, id, token, at: now, openedAt }) })).json();
  if (!previous?.id || previous.id === id) return;
  let removed;
  if (env.DISCORD_BOT_TOKEN) {
    removed = await fetch(`${api}/channels/${channel}/messages/${previous.id}`, { method: 'DELETE', headers: { Authorization: `Bot ${env.DISCORD_BOT_TOKEN}` } });
  } else if (previous.token && now - previous.at < MENU_TOKEN_LIFE) {
    removed = await fetch(`${api}/webhooks/${env.DISCORD_APP_ID}/${previous.token}/messages/@original`, { method: 'DELETE' });
  }
  if (removed && !removed.ok && removed.status !== 404) console.error(`Discord respondeu ${removed.status} ao apagar o menu anterior`);
}

const registryOf = (env) => env.REGISTRY.get(env.REGISTRY.idFromName('registry'));
const ask = async (stub, route, body) => (await stub.fetch(`https://r${route}`, { method: 'POST', body: JSON.stringify(body ?? {}) })).json();
const bearer = (request) => (request.headers.get('Authorization') ?? '').replace(/^Bearer\s+/i, '');

// Quem está chamando do plugin. O token do dono (fco_...) vem da ligação por código. A chave antiga da FC
// (FC_KEY) ainda vale por uma versão, só para a FC antiga.
async function pluginCaller(request, env) {
  const token = bearer(request);
  if (token.startsWith('fco_')) {
    const entry = await ask(registryOf(env), '/caller', { token });
    return entry ? { fc: entry.fc, owner: entry.owner, gameFcId: entry.gameFcId, status: entry.status, role: entry.role } : null;
  }
  return isAuthorized(request, env) ? { fc: LEGACY_FC, legacy: true } : null;
}

// Rotas da ligação, chamadas pelo plugin. Ver plano/CONTRATO-LIGACAO.md.
async function handleLink(request, env, pathname) {
  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: 'invalid json' }, 400);
  }
  const registry = registryOf(env);
  const ip = request.headers.get('CF-Connecting-IP') ?? null;
  const fcStub = (fc) => env.FC_STATE.get(env.FC_STATE.idFromName(fc));
  const status = { invalid: 400, 'unknown-code': 404, 'other-fc': 409, 'too-many': 429 };

  if (pathname === '/link/check') {
    const result = await ask(registry, '/check', { code: body.code, gameFcId: body.fcId, owner: body.owner, ip });
    if (result.error) return json({ error: result.error, fcName: result.fcName ?? null, serverName: result.serverName ?? null }, status[result.error]);
    return json({ code: result.code, fcName: result.fcName, serverName: result.serverName, firstLink: result.sameFc === null });
  }
  if (pathname === '/link/confirm') {
    const result = await ask(registry, '/confirm', {
      code: body.code, gameFcId: body.fcId, gameFcName: cleanFcName(body.fcName), owner: body.owner, ip, requester: body.requester,
    });
    if (result.error) return json({ error: result.error, fcName: result.fcName ?? null, serverName: result.serverName ?? null }, status[result.error]);
    await ask(fcStub(result.fc), '/link/owner', { owner: body.owner, characters: charactersOf(body.characters, body.fcId) });
    // status "pending": pedido enviado, esperando o dono da FC ou um admin aceitar no plugin.
    return json({
      token: result.token, fcName: result.fcName, serverName: result.serverName, relinked: result.relinked, status: result.status, role: result.role,
    });
  }

  const caller = await pluginCaller(request, env);
  if (!caller?.owner) return json({ error: 'unauthorized' }, 401);
  if (pathname === '/link/account') {
    const result = await ask(fcStub(caller.fc), '/link/account', { owner: caller.owner, code: body.code });
    return json(result, result.ok ? 200 : 404);
  }
  if (pathname === '/link/me') return json(await ask(registry, '/me', { token: bearer(request) }));
  if (pathname === '/link/members') {
    const result = await ask(registry, '/members', { token: bearer(request) });
    return json(result, result.error ? 403 : 200);
  }
  if (pathname === '/link/manage') {
    const result = await ask(registry, '/manage', { token: bearer(request), action: body.action, member: body.member });
    if (result.ok && result.owner) await ask(fcStub(result.fc), '/link/remove', { owner: result.owner });
    return json(result.ok ? { ok: true } : result, result.ok ? 200 : result.error === 'unknown-member' ? 404 : 403);
  }
  if (pathname === '/link/leave') {
    const result = await ask(registry, '/leave', { token: bearer(request) });
    if (result.ok) await ask(fcStub(result.fc), '/link/remove', { owner: result.owner });
    return json({ ok: result.ok });
  }
  return json({ error: 'not found' }, 404);
}

// Nome do servidor, para o plugin mostrar "FC X no servidor Y". Só com o bot; sem ele, fica sem nome.
async function guildName(env, guildId) {
  if (!env.DISCORD_BOT_TOKEN) return null;
  try {
    const response = await fetch(`${env.DISCORD_API ?? DISCORD_API}/guilds/${guildId}`, { headers: { Authorization: `Bot ${env.DISCORD_BOT_TOKEN}` } });
    return response.ok ? ((await response.json()).name ?? null) : null;
  } catch {
    return null;
  }
}

const cleanFcName = (value) => (typeof value === 'string' ? value.replace(/[\u0000-\u001f]/g, '').trim().slice(0, 40) || null : null);

export default {
  async fetch(request, env, ctx) {
    const { pathname } = new URL(request.url);
    if (request.method === 'POST' && pathname === '/interactions') return handleInteraction(request, env, ctx);
    if (request.method === 'POST' && pathname.startsWith('/link/')) return handleLink(request, env, pathname);

    const caller = await pluginCaller(request, env);
    if (!caller) return json({ error: 'unauthorized' }, 401);

    // Com o token do dono: só o que o plugin usa. Reenviar o painel, agrupar e baixar logs ficam com a chave antiga.
    const forOwner = (request.method === 'POST' && ['/update', '/log'].includes(pathname))
      || (request.method === 'GET' && ['/history', '/state'].includes(pathname));
    const forKey = (request.method === 'POST' && ['/repost', '/regroup'].includes(pathname))
      || (request.method === 'GET' && pathname === '/logs');
    if (!(forOwner || (caller.legacy && forKey))) return json({ error: 'not found' }, 404);
    // Pedido ainda não aceito: nada entra nem sai até o dono da FC ou um admin aceitar.
    if (caller.status === 'pending') return json({ error: 'pending' }, 403);

    const stub = env.FC_STATE.get(env.FC_STATE.idFromName(caller.fc));
    // Envio de um personagem de outra FC (alt): fica de fora desta FC, sem erro para o plugin.
    if (request.method === 'POST' && caller.gameFcId) {
      const text = await request.text();
      let fcId = null;
      try {
        fcId = JSON.parse(text)?.fcId ?? null;
      } catch {
        // O objeto da FC responde ao JSON inválido.
      }
      if (fcId != null && String(fcId) !== caller.gameFcId) return json({ ok: true, stored: false, reason: 'other-fc' }, 202);
      return stub.fetch(new Request(request.url, { method: 'POST', headers: request.headers, body: text }));
    }
    // Todo pedido da FC passa pelo mesmo objeto, que é o único ponto de gravação.
    return stub.fetch(request);
  },
};

export class FcState extends DurableObject {
  async fetch(request) {
    switch (`${request.method} ${new URL(request.url).pathname}`) {
      case 'POST /update': return this.handleUpdate(request);
      case 'POST /repost': return json(await this.repostPanel());
      case 'POST /regroup': return json(await this.regroupAlerts());
      case 'POST /log': return this.handleLog(request);
      case 'POST /interact': return this.handleInteract(request);
      case 'POST /menu': return this.handleMenu(request);
      case 'POST /link/owner': return this.handleLinkOwner(request);
      case 'POST /link/account': return this.handleLinkAccount(request);
      case 'POST /link/remove': return this.handleLinkRemove(request);
      case 'GET /state': return this.handleState();
      case 'GET /logs': return this.handleLogs();
      default: return this.handleHistory(request);
    }
  }

  // Guarda o último log de diagnóstico de cada personagem, só os mais recentes.
  async handleLog(request) {
    const text = await request.text();
    if (text.length > MAX_BODY_BYTES) return json({ error: 'too large' }, 413);

    let body;
    try {
      body = JSON.parse(text);
    } catch {
      return json({ error: 'invalid json' }, 400);
    }
    const character = typeof body?.character === 'string' ? body.character.trim().slice(0, MAX_LOG_NAME) : '';
    if (!character || typeof body.text !== 'string') return json({ error: 'invalid log' }, 400);

    const storage = this.ctx.storage;
    await storage.put(`log:${character}`, { character, uploadedAt: nowSeconds(), text: body.text });

    const logs = [...(await storage.list({ prefix: 'log:' })).entries()].sort((a, b) => b[1].uploadedAt - a[1].uploadedAt);
    if (logs.length > MAX_LOGS) await storage.delete(logs.slice(MAX_LOGS).map(([key]) => key));
    return json({ ok: true });
  }

  async handleLogs() {
    const logs = [...(await this.ctx.storage.list({ prefix: 'log:' })).values()].sort((a, b) => b.uploadedAt - a.uploadedAt);
    return json({ logs });
  }

  // Leitura para o plugin: estado atual e viagens dos últimos dias.
  async handleState() {
    const now = nowSeconds();
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const voyages = await this.voyagesSince(now, 7);
    return json({ ...publicState(state, voyages, now, await this.profit(now)), events: eventsForPlugin(await this.events()) });
  }

  // Viagens dos últimos dias, do mais antigo para hoje.
  async voyagesSince(now, count) {
    const voyages = [];
    for (const day of recentDays(now, count)) voyages.push(...(await this.voyagesOn(day)));
    return voyages;
  }

  // O lucro das 4 semanas fica em memória até chegar viagem nova ou o dia virar.
  async profit(now) {
    const day = dayOf(now);
    if (this.profitCache?.day !== day) this.profitCache = { day, report: profitReport(await this.voyagesSince(now, 28), now) };
    return this.profitCache.report;
  }

  async handleUpdate(request) {
    const text = await request.text();
    if (text.length > MAX_BODY_BYTES) return json({ error: 'too large' }, 413);

    let body;
    try {
      body = JSON.parse(text);
    } catch {
      return json({ error: 'invalid json' }, 400);
    }
    if (!isValidUpdate(body)) return json({ error: 'invalid update' }, 400);

    const storage = this.ctx.storage;
    const state = (await storage.get('state')) ?? newState();
    applyUpdate(state, body, nowSeconds());

    const changes = { state };
    for (const incoming of (body.voyages ?? []).filter((v) => isRealSlot(v.slot))) {
      const key = voyageKey(incoming);
      const existing = changes[key] ?? (await storage.get(key));
      const merged = mergeVoyage(existing, incoming);
      if (!merged) continue;
      // A rota e os tanques ficam na viagem, para o lucro por rota. Vêm do sub na hora em que o loot chega.
      const details = incoming.airship ? null : state.subs[incoming.slot]?.details;
      const route = existing?.route ?? details?.route;
      if (route) Object.assign(merged, { route, tanks: existing?.tanks ?? details?.tanks ?? 0 });
      changes[key] = merged;
      if (!existing) addNotice(state, sectorNotice(merged), merged.returnTime, nowSeconds());
    }
    if (Object.keys(changes).length > 1) this.profitCache = null;
    await storage.put(changes);

    const soon = Date.now() + DEBOUNCE_MS;
    const alarm = await storage.getAlarm();
    if (alarm === null || alarm > soon) await storage.setAlarm(soon);

    return json({ ok: true });
  }

  // Apaga a mensagem do painel e manda outra, que fica no fim do canal.
  async repostPanel() {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    if (state.messageId) {
      const response = await transportFor(state.panelVia, this.env)?.remove(state.messageId);
      // 404: alguém já apagou a mensagem.
      if (response && !response.ok && response.status !== 404) return { error: `discord ${response.status}` };
    }

    const latest = (await this.ctx.storage.get('state')) ?? newState();
    delete latest.messageId;
    delete latest.lastPanel;
    await this.ctx.storage.put('state', latest);
    await this.ctx.storage.setAlarm(Date.now());
    return { ok: true };
  }

  // O bot passa a postar o painel neste canal. O painel antigo (do webhook ou de outro canal) é apagado.
  async movePanel(channelId) {
    await this.repostPanel();
    const latest = (await this.ctx.storage.get('state')) ?? newState();
    latest.settings = { ...(latest.settings ?? {}), panelChannel: channelId };
    await this.ctx.storage.put('state', latest);
    await this.ctx.storage.setAlarm(Date.now());
  }

  // Apaga todos os avisos ainda no canal e manda de novo, agrupados, só os de sub das últimas 8 horas.
  // Os avisos refeitos mostram o @Subs sem notificar de novo.
  async regroupAlerts() {
    const storage = this.ctx.storage;
    const now = nowSeconds();
    const state = (await storage.get('state')) ?? newState();
    // Avisos antigos, sem o tipo gravado, também são trocados.
    const removed = [];
    for (const alert of (state.alerts ?? []).filter((a) => !a.kind || ['subReady', 'subForgotten', 'repairNeeded'].includes(a.kind))) {
      const response = await transportFor(alert.via, this.env)?.remove(alert.id);
      if (!response || response.ok || response.status === 404) removed.push(alert.id);
      await new Promise((resolve) => { setTimeout(resolve, 300); });
    }

    const sent = [];
    const transport = transportOf('alerts', state.settings, this.env);
    for (const alert of transport ? recentSubAlerts(state, now) : []) {
      const payload = { ...alertPayload(alert, this.env.ROLE_ID), allowed_mentions: { parse: [], roles: [] } };
      const response = await transport.create(payload);
      if (response.ok) sent.push({ id: response.id, deleteAt: alertDeleteAt(now), kind: alert.kind, via: transport.via });
      await new Promise((resolve) => { setTimeout(resolve, 600); });
    }

    const latest = (await storage.get('state')) ?? newState();
    latest.alerts = [...(latest.alerts ?? []).filter((a) => !removed.includes(a.id)), ...sent];
    await storage.put('state', latest);
    return { ok: true, removed: removed.length, sent: sent.length };
  }

  async handleHistory(request) {
    const day = new URL(request.url).searchParams.get('day') ?? dayOf(nowSeconds());
    if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return json({ error: 'invalid day' }, 400);
    return json({ day, voyages: await this.voyagesOn(day) });
  }

  async voyagesOn(day) {
    return [...(await this.ctx.storage.list({ prefix: `voyage:${day}:` })).values()].filter((v) => isRealSlot(v.slot));
  }

  async alarm() {
    const now = nowSeconds();
    let retryAt = null;
    try {
      await this.checkDalamud(now);
      await this.priceVoyages(now);
      retryAt = await this.publish(now);
      const alertRetryAt = await this.sendAlerts(now);
      await this.deleteOldAlerts(now);
      const weeklyRetryAt = await this.sendWeekly(now);
      await this.pruneVoyages(now);
      this.eventsAt = await this.runEvents(now);
      retryAt = retryAt ?? alertRetryAt ?? weeklyRetryAt;
    } catch (error) {
      console.error('Falha ao publicar o painel', error);
      retryAt = Date.now() + RETRY_MS;
    }

    const state = (await this.ctx.storage.get('state')) ?? newState();
    const next = retryAt ?? Math.min(nextEvent(state, nowSeconds()), this.eventsAt ?? Infinity) * 1000;

    // Uma atualização recebida durante a publicação pode já ter agendado algo mais cedo.
    const alarm = await this.ctx.storage.getAlarm();
    if (alarm === null || alarm > next) await this.ctx.storage.setAlarm(next);
  }

  // Um aviso por tipo, juntando o que ficou pronto junto. Devolve quando tentar de novo, ou null.
  async sendAlerts(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const transport = transportOf('alerts', state.settings, this.env);
    if (!transport) return null;
    for (const alert of pendingAlerts(state, now)) {
      const response = await transport.create(alertPayload(alert, this.env.ROLE_ID));
      if (response.retryAt) return response.retryAt;
      if (!response.ok) {
        console.error(`Discord respondeu ${response.status} ao avisar`);
        return Date.now() + RETRY_MS;
      }

      // O estado pode ter mudado durante o envio. Marca só o que entrou neste aviso.
      const latest = (await this.ctx.storage.get('state')) ?? newState();
      markSent(latest, alert);
      latest.alerts = [...(latest.alerts ?? []), { id: response.id, deleteAt: alertDeleteAt(now), kind: alert.kind, via: transport.via }];
      await this.ctx.storage.put('state', latest);
    }
    return null;
  }

  // Resumo da semana, toda segunda às 9h. Não marca ninguém e não se apaga como os avisos.
  async sendWeekly(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const due = weeklyDue(state, now);
    if (!due) return null;
    if (due.baseline) {
      const latest = (await this.ctx.storage.get('state')) ?? newState();
      latest.weekly = { ...(latest.weekly ?? {}), sent: due.id };
      await this.ctx.storage.put('state', latest);
      return null;
    }
    const transport = transportOf('panel', state.settings, this.env);
    if (!transport) return null;
    const payload = weeklySummary(state, await this.voyagesSince(now, 16), now);
    const response = await transport.create(payload);
    if (!response.ok) {
      console.error(`Discord respondeu ${response.status} ao resumo semanal`);
      return Date.now() + RETRY_MS;
    }
    const latest = (await this.ctx.storage.get('state')) ?? newState();
    latest.weekly = { ...(latest.weekly ?? {}), sent: due.id };
    await this.ctx.storage.put('state', latest);
    return null;
  }

  // Viagens com mais de 120 dias saem do histórico, uma vez por dia.
  async pruneVoyages(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const today = dayOf(now);
    if (state.prunedDay === today) return;
    const cutoff = dayOf(now - (VOYAGE_KEEP_DAYS * 86400));
    const old = [...(await this.ctx.storage.list({ prefix: 'voyage:', end: `voyage:${cutoff}` })).keys()];
    for (let n = 0; n < old.length; n += 128) await this.ctx.storage.delete(old.slice(n, n + 128));
    const latest = (await this.ctx.storage.get('state')) ?? newState();
    latest.prunedDay = today;
    await this.ctx.storage.put('state', latest);
  }

  async deleteOldAlerts(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const due = (state.alerts ?? []).filter((a) => a.deleteAt <= now);
    if (due.length === 0) return;

    const removed = [];
    for (const alert of due) {
      const response = await transportFor(alert.via, this.env)?.remove(alert.id);
      // 404: alguém já apagou a mensagem. Sem transporte (webhook ou bot tirado), não há como apagar.
      if (!response || response.ok || response.status === 404) removed.push(alert.id);
    }

    const latest = (await this.ctx.storage.get('state')) ?? newState();
    latest.alerts = (latest.alerts ?? []).filter((a) => !removed.includes(a.id));
    await this.ctx.storage.put('state', latest);
  }

  // Compara a versão do jogo (Thaliak) com a que o Dalamud suporta (goatcorp). Uma falha só adia a conferência.
  async checkDalamud(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    if (!dalamudDue(state, now)) return;
    try {
      const [dalamud, game] = await Promise.all([
        fetch('https://kamori.goats.dev/Dalamud/Release/VersionInfo?track=release').then((r) => (r.ok ? r.json() : null)),
        fetch('https://thaliak.xiv.dev/graphql/2022-08-14', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ query: '{ repository(slug: "4e9a232b") { latestVersion { versionString } } }' }),
        }).then((r) => (r.ok ? r.json() : null)),
      ]);
      const supported = dalamud?.supportedGameVer;
      const latest = game?.data?.repository?.latestVersion?.versionString;
      const latestState = (await this.ctx.storage.get('state')) ?? newState();
      if (supported && latest) applyDalamud(latestState, supported, latest, now);
      else latestState.dalamud = { ...(latestState.dalamud ?? {}), checkedAt: now };
      await this.ctx.storage.put('state', latestState);
    } catch (error) {
      console.error('Falha ao conferir o Dalamud', error);
    }
  }

  // Busca no Universalis o preço dos itens de market das viagens recentes. Uma falha não impede o painel de sair.
  async priceVoyages(now) {
    const storage = this.ctx.storage;
    const pending = [];
    for (const day of recentDays(now, 2)) {
      for (const [key, voyage] of await storage.list({ prefix: `voyage:${day}:` })) {
        if (needsPrice(voyage) && !voyage.priceChecked) pending.push([key, voyage]);
      }
    }
    if (pending.length === 0) return;

    const state = (await storage.get('state')) ?? newState();
    const changes = {};
    for (const world of new Set(pending.map(([, v]) => v.world))) {
      const voyages = pending.filter(([, v]) => v.world === world);
      const ids = [...new Set(voyages.flatMap(([, v]) => v.items.filter((i) => i.market).map((i) => i.itemId)))];
      const prices = await this.prices(world, ids, now);
      if (!prices) continue;
      for (const [key, voyage] of voyages) {
        const { voyage: priced, notice } = priceVoyage(voyage, prices);
        changes[key] = { ...priced, priceChecked: true };
        addNotice(state, notice, voyage.returnTime, now);
      }
    }

    if (Object.keys(changes).length === 0) return;
    const latest = (await storage.get('state')) ?? newState();
    latest.notices = [...(latest.notices ?? []), ...(state.notices ?? []).filter((n) => !(latest.notices ?? []).some((l) => l.id === n.id))];
    await storage.put({ ...changes, state: latest });
  }

  // Preço por unidade, guardado por 6 horas para não consultar o Universalis a cada viagem. Null se a consulta falhar.
  async prices(world, ids, now) {
    const storage = this.ctx.storage;
    const prices = {};
    const missing = [];
    for (const id of ids) {
      const cached = await storage.get(`price:${world}:${id}`);
      if (cached && now - cached.at < MARKET_CACHE) {
        if (cached.price > 0) prices[id] = cached.price;
      } else {
        missing.push(id);
      }
    }
    if (missing.length === 0) return prices;

    try {
      const fields = 'items.itemID,items.averagePrice,items.averagePriceNQ,items.minPrice,itemID,averagePrice,averagePriceNQ,minPrice';
      const response = await fetch(`${UNIVERSALIS}/${encodeURIComponent(world)}/${missing.join(',')}?listings=0&entries=20&fields=${fields}`, {
        headers: { 'User-Agent': 'FcTimers (github.com/LucianoBentrup/FcTimers)' },
      });
      if (!response.ok) {
        console.error(`Universalis respondeu ${response.status}`);
        return null;
      }
      const found = parseUniversalis(await response.json());
      const cache = {};
      for (const id of missing) {
        cache[`price:${world}:${id}`] = { price: found[id] ?? 0, at: now };
        if (found[id] != null) prices[id] = found[id];
      }
      await storage.put(cache);
      return prices;
    } catch (error) {
      console.error('Falha ao consultar o Universalis', error);
      return null;
    }
  }

  // Devolve quando tentar de novo, ou null se o painel ficou certo.
  // Pelo bot, o painel ganha os botões embaixo. Se o painel trocou de caminho (webhook para bot, ou de canal),
  // o antigo é apagado e sai um novo.
  async publish(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const transport = transportOf('panel', state.settings, this.env);
    if (!transport) return null;
    const panelBody = renderPanel(state, await this.voyagesOn(dayOf(now)), now);
    if (transport.via !== 'webhook') panelBody.components = panelComponents(langOf(state));
    const panel = JSON.stringify(panelBody);
    const sameVia = (state.panelVia ?? 'webhook') === transport.via;
    if (state.messageId && sameVia && state.lastPanel === panel) return null;

    let messageId = sameVia ? state.messageId : null;
    if (state.messageId && !sameVia) await transportFor(state.panelVia, this.env)?.remove(state.messageId);
    let response = null;

    if (messageId) {
      response = await transport.edit(messageId, panelBody);
      // A mensagem foi apagada: cria outra.
      if (response.status === 404) messageId = null;
    }
    if (!messageId) response = await transport.create(panelBody);

    if (response.retryAt) return response.retryAt;
    if (!response.ok) {
      console.error(`Discord respondeu ${response.status}`);
      return Date.now() + RETRY_MS;
    }
    if (!messageId) messageId = response.id;

    // O estado pode ter mudado durante o envio, então só estes campos são gravados.
    const latest = (await this.ctx.storage.get('state')) ?? newState();
    latest.messageId = messageId;
    latest.lastPanel = panel;
    latest.panelVia = transport.via;
    await this.ctx.storage.put('state', latest);
    return null;
  }
}

Object.assign(FcState.prototype, InteractMethods);
