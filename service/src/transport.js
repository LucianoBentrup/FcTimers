// Para onde vão as mensagens do serviço. Dois jeitos, com a mesma cara:
//   webhook: o de sempre, configurado no DISCORD_WEBHOOK. Não aceita botões.
//   bot:     o usuário de bot do app, num canal escolhido pelo /fcc. Aceita botões e manda DM.
// Cada chamada devolve { ok, status, id, retryAt }. Nada aqui lê ou grava o estado.

export const DISCORD_API = 'https://discord.com/api/v10';

async function result(response) {
  const out = { ok: response.ok, status: response.status, id: null, retryAt: null };
  if (response.status === 429) {
    const seconds = Number(response.headers.get('Retry-After')) || 5;
    out.retryAt = Date.now() + ((seconds + 1) * 1000);
  }
  if (response.ok && response.status !== 204) {
    try {
      out.id = (await response.json()).id ?? null;
    } catch {
      // Resposta sem corpo.
    }
  }
  return out;
}

const JSON_HEADERS = { 'Content-Type': 'application/json' };

export function webhookTransport(url) {
  const base = url.trim().split('?')[0];
  return {
    via: 'webhook',
    create: async (payload) => result(await fetch(`${base}?wait=true`, { method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(payload) })),
    edit: async (id, payload) => result(await fetch(`${base}/messages/${id}`, { method: 'PATCH', headers: JSON_HEADERS, body: JSON.stringify(payload) })),
    remove: async (id) => result(await fetch(`${base}/messages/${id}`, { method: 'DELETE' })),
  };
}

export function botTransport(token, channelId, api = DISCORD_API) {
  const headers = { ...JSON_HEADERS, Authorization: `Bot ${token}` };
  const base = `${api}/channels/${channelId}/messages`;
  return {
    via: `bot:${channelId}`,
    create: async (payload) => result(await fetch(base, { method: 'POST', headers, body: JSON.stringify(payload) })),
    edit: async (id, payload) => result(await fetch(`${base}/${id}`, { method: 'PATCH', headers, body: JSON.stringify(payload) })),
    remove: async (id) => result(await fetch(`${base}/${id}`, { method: 'DELETE', headers })),
  };
}

// DM: o Discord pede para abrir o canal com a pessoa antes de mandar.
export async function sendDm(token, userId, payload, api = DISCORD_API) {
  const headers = { ...JSON_HEADERS, Authorization: `Bot ${token}` };
  const opened = await result(await fetch(`${api}/users/@me/channels`, { method: 'POST', headers, body: JSON.stringify({ recipient_id: userId }) }));
  if (!opened.ok) return opened;
  return botTransport(token, opened.id, api).create(payload);
}

// Transporte de uma mensagem já enviada, pelo `via` gravado com ela. Mensagem antiga, sem `via`, é do webhook.
export function transportFor(via, env) {
  if (via?.startsWith('bot:')) return env.DISCORD_BOT_TOKEN ? botTransport(env.DISCORD_BOT_TOKEN, via.slice(4), env.DISCORD_API ?? DISCORD_API) : null;
  return env.DISCORD_WEBHOOK ? webhookTransport(env.DISCORD_WEBHOOK) : null;
}

// Onde sai cada tipo de mensagem agora. Com o bot configurado e o canal escolhido, pelo bot; senão, pelo webhook.
// kind: 'panel' | 'alerts' | 'events'. Alertas sem canal próprio vão para o canal do painel.
export function channelFor(kind, settings = {}) {
  if (kind === 'events') return settings.eventsChannel ?? null;
  if (kind === 'alerts') return settings.alertsChannel ?? settings.panelChannel ?? null;
  return settings.panelChannel ?? null;
}

export function transportOf(kind, settings, env) {
  const channel = channelFor(kind, settings);
  if (channel && env.DISCORD_BOT_TOKEN) return botTransport(env.DISCORD_BOT_TOKEN, channel, env.DISCORD_API ?? DISCORD_API);
  if (kind === 'events') return null;
  return env.DISCORD_WEBHOOK ? webhookTransport(env.DISCORD_WEBHOOK) : null;
}
