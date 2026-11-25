import { DurableObject } from 'cloudflare:workers';
import {
  alertDeleteAt, alertPayload, applyUpdate, dayOf, isRealSlot, isValidUpdate, MARKET_CACHE, markSent, mergeVoyage, needsPrice, newState,
  applyDalamud, dalamudDue, lootSummary, nextEvent, parseUniversalis, subsSummary, suppliesSummary, pendingAlerts, priceVoyage, publicState, recentDays, recentSubAlerts, renderPanel, sectorNotice, voyageKey,
  profitReport, profitSummary, VOYAGE_KEEP_DAYS, weeklyDue, weeklySummary,
} from './logic.js';
import {
  canManage, COMMAND, EPHEMERAL, optionValue, PING, privateReply, REPLY, verifyDiscord,
} from './discord.js';
import {
  DEFERRED_UPDATE_MESSAGE, fcFor, langOf, LEGACY, MESSAGE_COMPONENT, menuMessage, parseCustomId, sectionEmbed, sharedMessage, text,
  UPDATE_MESSAGE, userOf,
} from './menu.js';

const DEBOUNCE_MS = 2000;
const RETRY_MS = 60000;
const MAX_BODY_BYTES = 256 * 1024;
const MAX_LOGS = 10;
const MAX_LOG_NAME = 40;
// Viagens de até 1 dia atrás ainda geram aviso de setor novo e de item de market.
const NOTICE_WINDOW = 86400;
const UNIVERSALIS = 'https://universalis.app/api/v2';
// O teste local troca por um Discord simulado (DISCORD_API no .dev.vars).
const DISCORD_API = 'https://discord.com/api/v10';

function addNotice(state, notice, returnTime, now) {
  if (!notice || now - returnTime > NOTICE_WINDOW) return;
  state.notices ??= [];
  if (!state.notices.some((n) => n.id === notice.id)) state.notices.push(notice);
}

const json = (body, status = 200) => Response.json(body, { status });
const nowSeconds = () => Math.floor(Date.now() / 1000);

// Título do loot no idioma da FC: "Loot de hoje (25/11)", "Loot of the last 7 days".
function lootTitle(lang, count, now) {
  const [, month, day] = dayOf(now).split('-');
  if (lang === 'en') return count === 1 ? `Loot today (${day}/${month})` : `Loot of the last ${count} days`;
  return count === 1 ? `Loot de hoje (${day}/${month})` : `Loot dos últimos ${count} dias`;
}

function isAuthorized(request, env) {
  const expected = new TextEncoder().encode(`Bearer ${env.FC_KEY}`);
  const received = new TextEncoder().encode(request.headers.get('Authorization') ?? '');
  return env.FC_KEY && expected.byteLength === received.byteLength && crypto.subtle.timingSafeEqual(expected, received);
}

// Comandos e botões do Discord. A assinatura do app substitui a chave da FC nesta rota.
async function handleInteraction(request, env, ctx) {
  const body = await request.text();
  const signed = await verifyDiscord(
    env.DISCORD_PUBLIC_KEY, request.headers.get('X-Signature-Ed25519'), request.headers.get('X-Signature-Timestamp'), body,
  );
  if (!signed) return new Response('invalid request signature', { status: 401 });

  const interaction = JSON.parse(body);
  if (interaction.type === PING) return json({ type: PING });

  const fc = fcFor(interaction, env);
  if (!fc) return json(privateReply(`${text('en', 'notLinked')}\n${text('pt', 'notLinked')}`));
  const stub = env.FC_STATE.get(env.FC_STATE.idFromName(fc));
  const internal = (route, payload) => stub.fetch(`https://fc/${route}`, { method: 'POST', body: payload ? JSON.stringify(payload) : undefined });
  const viewModel = async (payload) => (await internal('menu', payload)).json();
  const manager = canManage(interaction);

  // Abre o menu numa mensagem nova (comando) ou troca o conteúdo da mensagem do botão.
  const show = async (type, { view, arg, notice, action }) => {
    const model = await viewModel({ view, arg, action });
    const data = menuMessage(model.lang, { ...model, view, arg, manager, notice: notice?.(model.lang) ?? model.notice });
    return json({ type, data });
  };

  if (interaction.type === COMMAND) {
    const name = interaction.data?.name;
    if (name === 'fcc') return show(REPLY, { view: 'panel' });
    // Comando antigo ainda na lista do Discord: abre a seção equivalente do /fcc.
    const legacy = LEGACY[name];
    if (!legacy) return json(privateReply(text('en', 'unknown')));
    const view = legacy.view === 'manage' && !manager ? 'panel' : legacy.view;
    const arg = legacy.args?.[optionValue(interaction, 'periodo')];
    return show(REPLY, { view, arg, notice: (lang) => text(lang, 'moved') });
  }

  if (interaction.type !== MESSAGE_COMPONENT) return json(privateReply(text('en', 'unknown')));
  const parsed = parseCustomId(interaction.data?.custom_id);
  if (!parsed) return json(privateReply(text('en', 'unknown')));
  // A permissão é conferida a cada clique: o menu pode ter sido aberto antes de a pessoa perder o cargo.
  if ((parsed.kind === 'action' || parsed.manager) && !manager) {
    return json(privateReply(`${text('en', 'managersOnly')}\n${text('pt', 'managersOnly')}`));
  }

  if (parsed.kind === 'view') return show(UPDATE_MESSAGE, parsed);
  if (parsed.kind === 'share') {
    const model = await viewModel(parsed);
    return json({ type: REPLY, data: sharedMessage(model.lang, model.embed, userOf(interaction)) });
  }

  switch (parsed.name) {
    case 'repost':
      await internal('repost');
      return show(UPDATE_MESSAGE, { view: 'manage', notice: (lang) => text(lang, 'panelReposted') });
    case 'regroup': {
      // Apagar e reenviar avisos passa dos 3 segundos que o Discord espera: responde já e termina depois.
      const original = `${env.DISCORD_API ?? DISCORD_API}/webhooks/${env.DISCORD_APP_ID}/${interaction.token}/messages/@original`;
      ctx.waitUntil((async () => {
        let notice;
        try {
          const result = await (await internal('regroup')).json();
          notice = (lang) => text(lang, 'regroupDone')(result.removed, result.sent);
        } catch (error) {
          console.error('Falha ao agrupar os avisos', error);
          notice = (lang) => text(lang, 'regroupFailed');
        }
        const { data } = await (await show(UPDATE_MESSAGE, { view: 'manage', notice })).json();
        delete data.flags;
        await fetch(original, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) });
      })());
      return json({ type: DEFERRED_UPDATE_MESSAGE });
    }
    case 'log': {
      const answer = await viewModel({ action: 'log' });
      if (!answer.file) return json(privateReply(answer.notice));
      const form = new FormData();
      form.append('payload_json', JSON.stringify({
        type: REPLY,
        data: { content: answer.notice, flags: EPHEMERAL, allowed_mentions: { parse: [] }, attachments: [{ id: 0, filename: answer.file.name }] },
      }));
      form.append('files[0]', new Blob([answer.file.text], { type: 'text/plain' }), answer.file.name);
      return new Response(form);
    }
    default:
      // Ligar e desligar o resumo e trocar o idioma: o objeto da FC grava e devolve os ajustes já mudados.
      return show(UPDATE_MESSAGE, { view: 'manage', action: parsed.name });
  }
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
      case 'POST /repost': return this.handleRepost();
      case 'POST /regroup': return this.handleRegroup();
      case 'POST /log': return this.handleLog(request);
      case 'POST /menu': return this.handleMenu(request);
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

  // O conteúdo de cada seção do /fcc e as ações que só mudam ajustes. Devolve o idioma da FC, o embed da seção e,
  // para os ajustes, o que os botões precisam saber (resumo ligado ou não).
  async handleMenu(request) {
    const { view = 'panel', arg, action } = await request.json();
    const now = nowSeconds();
    const storage = this.ctx.storage;
    let state = (await storage.get('state')) ?? newState();
    let notice;

    if (action === 'log') {
      const lang = langOf(state);
      const logs = [...(await storage.list({ prefix: 'log:' })).values()].sort((a, b) => b.uploadedAt - a.uploadedAt);
      const log = logs[0];
      if (!log) return json({ lang, notice: text(lang, 'noLog') });
      return json({
        lang,
        notice: text(lang, 'logOf')(log.character, log.uploadedAt),
        file: { name: `log-${log.character.replace(/[^\w-]+/g, '_')}.txt`, text: log.text },
      });
    }
    if (['weekly-on', 'weekly-off', 'lang-en', 'lang-pt'].includes(action)) {
      if (action.startsWith('weekly')) {
        const off = action === 'weekly-off';
        state.weekly = { ...(state.weekly ?? {}), off };
        notice = off ? 'weeklyNowOff' : 'weeklyNowOn';
      } else {
        state.settings = { ...(state.settings ?? {}), lang: action.slice(5) };
        notice = 'langNow';
      }
      await storage.put('state', state);
      // O resumo ligado ou desligado muda o próximo alarme.
      if (action.startsWith('weekly')) await storage.setAlarm(Date.now());
      state = (await storage.get('state')) ?? state;
    }

    const lang = langOf(state);
    return json({
      lang,
      embed: await this.sectionEmbedFor(view, arg, state, now, lang),
      weeklyOff: Boolean(state.weekly?.off),
      ...(notice ? { notice: text(lang, notice) } : {}),
    });
  }

  async sectionEmbedFor(view, arg, state, now, lang) {
    const t = (key) => text(lang, key);
    switch (view) {
      case 'subs': return sectionEmbed(t('subsTitle'), subsSummary(state, now));
      case 'supplies': return sectionEmbed(t('suppliesTitle'), suppliesSummary(state, now));
      case 'profit': return sectionEmbed(t('profitTitle'), profitSummary(await this.profit(now)));
      case 'manage': return sectionEmbed(t('manageTitle'), t('manageText'));
      case 'loot': {
        const count = { week: 7, month: 30 }[arg] ?? 1;
        return sectionEmbed(t('lootTitle'), lootSummary(await this.voyagesSince(now, count), lootTitle(lang, count, now)));
      }
      default: return renderPanel(state, await this.voyagesOn(dayOf(now)), now).embeds[0];
    }
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
    return json(publicState(state, voyages, now, await this.profit(now)));
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
    for (const alert of (state.alerts ?? []).filter((a) => !a.kind || ['subReady', 'subForgotten', 'repairNeeded'].includes(a.kind))) {
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
      const weeklyRetryAt = await this.sendWeekly(now);
      await this.pruneVoyages(now);
      retryAt = retryAt ?? alertRetryAt ?? weeklyRetryAt;
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
    const payload = weeklySummary(state, await this.voyagesSince(now, 16), now);
    const response = await fetch(this.webhook(), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
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
