import { DurableObject } from 'cloudflare:workers';
import {
  alertDeleteAt, alertPayload, applyUpdate, dayOf, isValidUpdate, mergeVoyage, newState, nextEvent, pendingReturns, renderPanel, voyageKey,
} from './logic.js';

const DEBOUNCE_MS = 2000;
const RETRY_MS = 60000;
const MAX_BODY_BYTES = 256 * 1024;

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
    const allowed = (request.method === 'POST' && pathname === '/update') || (request.method === 'GET' && pathname === '/history');
    if (!allowed) return json({ error: 'not found' }, 404);

    // Todo pedido passa pelo mesmo objeto, que é o único ponto de gravação.
    return env.FC_STATE.get(env.FC_STATE.idFromName('fc')).fetch(request);
  },
};

export class FcState extends DurableObject {
  async fetch(request) {
    return request.method === 'POST' ? this.handleUpdate(request) : this.handleHistory(request);
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
    for (const incoming of body.voyages ?? []) {
      const key = voyageKey(incoming);
      const merged = mergeVoyage(changes[key] ?? (await storage.get(key)), incoming);
      if (merged) changes[key] = merged;
    }
    await storage.put(changes);

    const soon = Date.now() + DEBOUNCE_MS;
    const alarm = await storage.getAlarm();
    if (alarm === null || alarm > soon) await storage.setAlarm(soon);

    return json({ ok: true });
  }

  async handleHistory(request) {
    const day = new URL(request.url).searchParams.get('day') ?? dayOf(nowSeconds());
    if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return json({ error: 'invalid day' }, 400);
    return json({ day, voyages: await this.voyagesOn(day) });
  }

  async voyagesOn(day) {
    return [...(await this.ctx.storage.list({ prefix: `voyage:${day}:` })).values()];
  }

  async alarm() {
    const now = nowSeconds();
    let retryAt = null;
    try {
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

  // Um aviso só para os submarinos que voltaram juntos. Devolve quando tentar de novo, ou null.
  async sendAlerts(now) {
    const state = (await this.ctx.storage.get('state')) ?? newState();
    const returned = pendingReturns(state, now);
    if (returned.length === 0) return null;

    const response = await fetch(`${this.webhook()}?wait=true`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(alertPayload(returned, this.env.ROLE_ID)),
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

    // O estado pode ter mudado durante o envio. Marca só as viagens que entraram neste aviso.
    const latest = (await this.ctx.storage.get('state')) ?? newState();
    for (const sub of Object.values(latest.subs)) {
      if (returned.some((r) => r.name === sub.name && r.returnTime === sub.returnTime)) sub.notified = sub.returnTime;
    }
    latest.alerts = [...(latest.alerts ?? []), { id, deleteAt: alertDeleteAt(latest, now) }];
    await this.ctx.storage.put('state', latest);
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
