// Registro das FCs: qual objeto de FC atende cada servidor do Discord, cada código de FC e cada plugin ligado.
// Um objeto só (idFromName('registry')), que não guarda dado de jogo nenhum, só os ponteiros.
//
//   guild:<id do servidor>      -> { fc, guildName }
//   code:<FCC-XXXXXX>           -> { fc }
//   fc:<nome do objeto>         -> { fc, guildId, guildName, code, gameFcId, gameFcName, createdAt }
//   token:<sha-256 do token>    -> { fc, owner, gameFcId, createdAt }
//   owner:<fc>:<dono>           -> { tokenHash }
//   fails:<dono ou ip>          -> { count, since }

import { DurableObject } from 'cloudflare:workers';
import {
  CODE_ATTEMPTS, CODE_WINDOW, isCode, isGameFcId, isOwner, newCode, newToken, normalizeCode, sha256,
} from './link.js';

const json = (body, status = 200) => Response.json(body, { status });
const nowSeconds = () => Math.floor(Date.now() / 1000);

// A FC que já existia antes do registro (a do DISCORD_GUILD_ID) continua no objeto 'fc', com o histórico.
export const LEGACY_FC = 'fc';

export class Registry extends DurableObject {
  async fetch(request) {
    const route = new URL(request.url).pathname;
    const body = request.method === 'POST' ? await request.json() : {};
    switch (route) {
      case '/guild': return json(await this.guild(body.guildId));
      case '/create': return json(await this.create(body));
      case '/code': return json(await this.setCode(body));
      case '/check': return json(await this.check(body));
      case '/confirm': return json(await this.confirm(body));
      case '/token': return json(await this.token(body.token));
      case '/leave': return json(await this.leave(body.token));
      default: return json({ error: 'not found' }, 404);
    }
  }

  async guild(guildId) {
    return (await this.ctx.storage.get(`guild:${guildId}`)) ?? null;
  }

  // Liga um servidor a uma FC. O servidor antigo (DISCORD_GUILD_ID) fica com a FC antiga; um novo ganha a sua.
  async create({ guildId, guildName, legacy }) {
    const existing = await this.guild(guildId);
    if (existing) return existing;
    const fc = legacy ? LEGACY_FC : `g:${guildId}`;
    const entry = { fc, guildName: guildName ?? null };
    await this.ctx.storage.put({
      [`guild:${guildId}`]: entry,
      [`fc:${fc}`]: { ...((await this.ctx.storage.get(`fc:${fc}`)) ?? {}), fc, guildId, guildName: guildName ?? null, createdAt: nowSeconds() },
    });
    return entry;
  }

  // Troca o código da FC (o antigo para de valer na hora) ou desliga, com `off`. Ligações já feitas continuam.
  async setCode({ fc, off }) {
    const record = (await this.ctx.storage.get(`fc:${fc}`)) ?? { fc };
    if (record.code) await this.ctx.storage.delete(`code:${record.code}`);
    record.code = null;
    if (!off) {
      let code = newCode();
      while (await this.ctx.storage.get(`code:${code}`)) code = newCode();
      record.code = code;
      await this.ctx.storage.put(`code:${code}`, { fc });
    }
    await this.ctx.storage.put(`fc:${fc}`, record);
    return { code: record.code };
  }

  // Limite de tentativas erradas, por dono do plugin e por IP: um código não dá para adivinhar no chute.
  async tooMany(keys) {
    const now = nowSeconds();
    for (const key of keys) {
      const fails = await this.ctx.storage.get(`fails:${key}`);
      if (fails && now - fails.since < CODE_WINDOW && fails.count >= CODE_ATTEMPTS) return true;
    }
    return false;
  }

  async fail(keys) {
    const now = nowSeconds();
    for (const key of keys) {
      const fails = await this.ctx.storage.get(`fails:${key}`);
      const fresh = !fails || now - fails.since >= CODE_WINDOW;
      await this.ctx.storage.put(`fails:${key}`, fresh ? { count: 1, since: now } : { ...fails, count: fails.count + 1 });
    }
  }

  // Confere o código sem ligar nada. Devolve o que o plugin mostra para confirmar: "FC X no servidor Y".
  async check({ code, gameFcId, owner, ip }) {
    if (!isOwner(owner) || !isGameFcId(gameFcId)) return { error: 'invalid' };
    const keys = [`owner:${owner}`, `ip:${ip ?? '?'}`];
    if (await this.tooMany(keys)) return { error: 'too-many' };
    const normalized = normalizeCode(code);
    const pointer = isCode(normalized) ? await this.ctx.storage.get(`code:${normalized}`) : null;
    if (!pointer) {
      await this.fail(keys);
      return { error: 'unknown-code' };
    }
    const record = await this.ctx.storage.get(`fc:${pointer.fc}`);
    // A primeira ligação grava qual FC do jogo é esta. Depois, só personagem da mesma FC liga.
    const sameFc = record.gameFcId ? record.gameFcId === gameFcId : null;
    return {
      fc: pointer.fc, code: normalized, fcName: record.gameFcName ?? null, serverName: record.guildName ?? null,
      sameFc, error: sameFc === false ? 'other-fc' : null,
    };
  }

  // Liga o dono à FC e devolve o token que substitui a chave da FC. Ligar de novo troca o token.
  async confirm({ code, gameFcId, gameFcName, owner, ip }) {
    const checked = await this.check({ code, gameFcId, owner, ip });
    if (checked.error) return checked;
    const record = await this.ctx.storage.get(`fc:${checked.fc}`);
    if (!record.gameFcId) Object.assign(record, { gameFcId, gameFcName: gameFcName ?? null });

    const token = newToken();
    const hash = await sha256(token);
    const ownerKey = `owner:${checked.fc}:${owner}`;
    const previous = await this.ctx.storage.get(ownerKey);
    if (previous) await this.ctx.storage.delete(`token:${previous.tokenHash}`);
    await this.ctx.storage.put({
      [`fc:${checked.fc}`]: record,
      [`token:${hash}`]: { fc: checked.fc, owner, gameFcId: record.gameFcId, createdAt: nowSeconds() },
      [ownerKey]: { tokenHash: hash },
    });
    return { fc: checked.fc, token, fcName: record.gameFcName, serverName: record.guildName ?? null, relinked: Boolean(previous) };
  }

  async token(token) {
    if (typeof token !== 'string' || token.length > 100) return null;
    return (await this.ctx.storage.get(`token:${await sha256(token)}`)) ?? null;
  }

  // O dono desliga o plugin desta FC: o token para de valer.
  async leave(token) {
    const entry = await this.token(token);
    if (!entry) return { ok: false };
    await this.ctx.storage.delete([`token:${await sha256(token)}`, `owner:${entry.fc}:${entry.owner}`]);
    return { ok: true, fc: entry.fc, owner: entry.owner };
  }
}
