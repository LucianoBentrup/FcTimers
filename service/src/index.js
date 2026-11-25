import { DurableObject } from 'cloudflare:workers';
import {
  alertDeleteAt, alertPayload, applyUpdate, dayOf, isRealSlot, isValidUpdate, MARKET_CACHE, markSent, mergeVoyage, needsPrice, newState,
  applyDalamud, dalamudDue, nextEvent, parseUniversalis, pendingAlerts, priceVoyage, publicState, recentDays, recentSubAlerts, renderPanel, sectorNotice, voyageKey,
  profitReport, VOYAGE_KEEP_DAYS, weeklyDue, weeklySummary,
} from './logic.js';
import {
  canManage, COMMAND, optionValue, PING, privateReply, verifyDiscord,
} from './discord.js';
import { eventsForPlugin, modalFields } from './events.js';
import { InteractMethods } from './interact.js';
import {
  DEFERRED_UPDATE_MESSAGE, fcFor, LEGACY, MESSAGE_COMPONENT, MODAL_SUBMIT, panelComponents, parseCustomId, text, userOf,
} from './menu.js';
import { langOf } from './texts.js';
import { DISCORD_API, transportFor, transportOf } from './transport.js';

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
  const fc = fcFor(interaction, env);
  if (!fc) return json(privateReply(both('notLinked')));

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
  // A permissão é conferida a cada clique: o menu pode ter sido aberto antes de a pessoa perder o cargo.
  if ((asked.kind === 'action' || asked.kind === 'eventrole' || asked.manager) && !manager) return json(privateReply(both('managersOnly')));

  const payload = {
    ...asked,
    user: userOf(interaction),
    manager,
    roles: interaction.member?.roles ?? [],
    guildId: interaction.guild_id,
    channelId: interaction.channel_id ?? interaction.channel?.id ?? null,
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

export default {
  async fetch(request, env, ctx) {
    const { pathname } = new URL(request.url);
    if (request.method === 'POST' && pathname === '/interactions') return handleInteraction(request, env, ctx);
    if (!isAuthorized(request, env)) return json({ error: 'unauthorized' }, 401);

    const allowed = (request.method === 'POST' && ['/update', '/repost', '/log', '/regroup'].includes(pathname))
      || (request.method === 'GET' && ['/history', '/state', '/logs'].includes(pathname));
    if (!allowed) return json({ error: 'not found' }, 404);

    // Todo pedido passa pelo mesmo objeto, que é o único ponto de gravação.
    return env.FC_STATE.get(env.FC_STATE.idFromName('fc')).fetch(request);
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
