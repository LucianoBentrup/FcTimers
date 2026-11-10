import { DurableObject } from 'cloudflare:workers';
import {
  alertDeleteAt, alertPayload, applyUpdate, dayOf, isRealSlot, isValidUpdate, MARKET_CACHE, markSent, mergeVoyage, needsPrice, newState,
  applyDalamud, dalamudDue, nextEvent, parseUniversalis, pendingAlerts, priceVoyage, publicState, recentDays, recentSubAlerts, renderPanel, sectorNotice, voyageKey,
} from './logic.js';

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

export default {
  async fetch(request, env) {
    if (!isAuthorized(request, env)) return json({ error: 'unauthorized' }, 401);

    const { pathname } = new URL(request.url);
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
      case 'POST /repost': return this.handleRepost();
      case 'POST /regroup': return this.handleRegroup();
      case 'POST /log': return this.handleLog(request);
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
    const voyages = [];
    for (const day of recentDays(now)) voyages.push(...(await this.voyagesOn(day)));
    return json(publicState(state, voyages, now));
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
      if (merged) changes[key] = merged;
      if (merged && !existing) addNotice(state, sectorNotice(merged), merged.returnTime, nowSeconds());
    }
    await storage.put(changes);

    const soon = Date.now() + DEBOUNCE_MS;
    const alarm = await storage.getAlarm();
    if (alarm === null || alarm > soon) await storage.setAlarm(soon);

    return json({ ok: true });
  }

  // Apaga a mensagem do painel e manda outra, que fica no fim do canal.
  async handleRepost() {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    if (state.messageId) {
      const response = await fetch(`${this.webhook()}/messages/${state.messageId}`, { method: 'DELETE' });
      // 404: alguém já apagou a mensagem.
      if (!response.ok && response.status !== 404) return json({ error: `discord ${response.status}` }, 502);
    }

    const latest = (await this.ctx.storage.get('state')) ?? newState();
    delete latest.messageId;
    delete latest.lastPanel;
    await this.ctx.storage.put('state', latest);
    await this.ctx.storage.setAlarm(Date.now());
    return json({ ok: true });
  }

  // Apaga todos os avisos ainda no canal e manda de novo, agrupados, só os de sub das últimas 8 horas.
  // Os avisos refeitos mostram o @Subs sem notificar de novo.
  async handleRegroup() {
    const storage = this.ctx.storage;
    const now = nowSeconds();
    const state = (await storage.get('state')) ?? newState();
    // Avisos antigos, sem o tipo gravado, também são trocados.
    const removed = [];
    for (const alert of (state.alerts ?? []).filter((a) => !a.kind || a.kind === 'subReady' || a.kind === 'subForgotten')) {
      const response = await fetch(`${this.webhook()}/messages/${alert.id}`, { method: 'DELETE' });
      if (response.ok || response.status === 404) removed.push(alert.id);
      await new Promise((resolve) => { setTimeout(resolve, 300); });
    }

    const sent = [];
    for (const alert of recentSubAlerts(state, now)) {
      const payload = { ...alertPayload(alert, this.env.ROLE_ID), allowed_mentions: { parse: [], roles: [] } };
      const response = await fetch(`${this.webhook()}?wait=true`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload),
      });
      if (response.ok) sent.push({ id: (await response.json()).id, deleteAt: alertDeleteAt(now), kind: alert.kind });
      await new Promise((resolve) => { setTimeout(resolve, 600); });
    }

    const latest = (await storage.get('state')) ?? newState();
    latest.alerts = [...(latest.alerts ?? []).filter((a) => !removed.includes(a.id)), ...sent];
    await storage.put('state', latest);
    return json({ ok: true, removed: removed.length, sent: sent.length });
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
      retryAt = retryAt ?? alertRetryAt;
    } catch (error) {
      console.error('Falha ao publicar o painel', error);
      retryAt = Date.now() + RETRY_MS;
    }

    const state = (await this.ctx.storage.get('state')) ?? newState();
    const next = retryAt ?? nextEvent(state, nowSeconds()) * 1000;

    // Uma atualização recebida durante a publicação pode já ter agendado algo mais cedo.
    const alarm = await this.ctx.storage.getAlarm();
    if (alarm === null || alarm > next) await this.ctx.storage.setAlarm(next);
  }

  webhook() {
    return this.env.DISCORD_WEBHOOK.trim().split('?')[0];
  }

  // Um aviso por tipo, juntando o que ficou pronto junto. Devolve quando tentar de novo, ou null.
  async sendAlerts(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    for (const alert of pendingAlerts(state, now)) {
      const response = await fetch(`${this.webhook()}?wait=true`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(alertPayload(alert, this.env.ROLE_ID)),
      });
      if (response.status === 429) {
        const seconds = Number(response.headers.get('Retry-After')) || 5;
        return Date.now() + ((seconds + 1) * 1000);
      }
      if (!response.ok) {
        console.error(`Discord respondeu ${response.status} ao avisar`);
        return Date.now() + RETRY_MS;
      }
      const { id } = await response.json();

      // O estado pode ter mudado durante o envio. Marca só o que entrou neste aviso.
      const latest = (await this.ctx.storage.get('state')) ?? newState();
      markSent(latest, alert);
      latest.alerts = [...(latest.alerts ?? []), { id, deleteAt: alertDeleteAt(now), kind: alert.kind }];
      await this.ctx.storage.put('state', latest);
    }
    return null;
  }

  async deleteOldAlerts(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const due = (state.alerts ?? []).filter((a) => a.deleteAt <= now);
    if (due.length === 0) return;

    const removed = [];
    for (const alert of due) {
      const response = await fetch(`${this.webhook()}/messages/${alert.id}`, { method: 'DELETE' });
      // 404: alguém já apagou a mensagem.
      if (response.ok || response.status === 404) removed.push(alert.id);
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
  async publish(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const panel = JSON.stringify(renderPanel(state, await this.voyagesOn(dayOf(now)), now));
    if (state.messageId && state.lastPanel === panel) return null;

    const webhook = this.webhook();
    const headers = { 'Content-Type': 'application/json' };
    let messageId = state.messageId;
    let response = null;

    if (messageId) {
      response = await fetch(`${webhook}/messages/${messageId}`, { method: 'PATCH', headers, body: panel });
      // A mensagem foi apagada: cria outra.
      if (response.status === 404) messageId = null;
    }
    if (!messageId) {
      response = await fetch(`${webhook}?wait=true`, { method: 'POST', headers, body: panel });
    }

    if (response.status === 429) {
      const seconds = Number(response.headers.get('Retry-After')) || 5;
      return Date.now() + ((seconds + 1) * 1000);
    }
    if (!response.ok) {
      console.error(`Discord respondeu ${response.status}`);
      return Date.now() + RETRY_MS;
    }
    if (!messageId) messageId = (await response.json()).id;

    // O estado pode ter mudado durante o envio, então só estes campos são gravados.
    const latest = (await this.ctx.storage.get('state')) ?? newState();
    latest.messageId = messageId;
    latest.lastPanel = panel;
    await this.ctx.storage.put('state', latest);
    return null;
  }
}
