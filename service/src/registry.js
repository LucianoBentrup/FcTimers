// Registro das FCs: qual objeto de FC atende cada servidor do Discord, cada código de FC e cada plugin ligado.
// Um objeto só (idFromName('registry')), que não guarda dado de jogo nenhum, só os ponteiros.
//
//   guild:<id do servidor>      -> { fc, guildName }
//   code:<FCC-XXXXXX>           -> { fc }
//   fc:<nome do objeto>         -> { fc, guildId, guildName, code, gameFcId, gameFcName, createdAt }
//   token:<sha-256 do token>    -> { fc, owner, gameFcId, createdAt }
//   owner:<fc>:<dono>           -> { tokenHash }
//   member:<fc>:<id do membro>  -> { owner, name, world, role, status, requestedAt, joinedAt }
//   fails:<dono ou ip>          -> { count, since }
//
// Membros: o código vira pedido (nick + mundo); dono ou admin aceita pelo plugin. Primeiro a ligar = dono da FC.
// id do membro = pedaço do hash do dono, pra lista não expor o ID real.

import { DurableObject } from 'cloudflare:workers';
import {
  CODE_ATTEMPTS, CODE_WINDOW, isCode, isGameFcId, isOwner, newCode, newToken, normalizeCode, sha256,
} from './link.js';

const json = (body, status = 200) => Response.json(body, { status });
const nowSeconds = () => Math.floor(Date.now() / 1000);

// A FC que já existia antes do registro (a do DISCORD_GUILD_ID) continua no objeto 'fc', com o histórico.
export const LEGACY_FC = 'fc';

// Quem pode aceitar pedidos e expulsar.
const MANAGERS = ['owner', 'admin'];
const memberId = async (owner) => (await sha256(`member:${owner}`)).slice(0, 16);
const cleanText = (value) => (typeof value === 'string' ? value.replace(/[\u0000-\u001f]/g, '').trim().slice(0, 40) : '');

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
      case '/caller': return json(await this.caller(body.token));
      case '/leave': return json(await this.leave(body.token));
      case '/me': return json(await this.me(body.token));
      case '/members': return json(await this.members(body.token));
      case '/manage': return json(await this.manage(body));
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
  async confirm({ code, gameFcId, gameFcName, owner, ip, requester }) {
    const checked = await this.check({ code, gameFcId, owner, ip });
    if (checked.error) return checked;
    const record = await this.ctx.storage.get(`fc:${checked.fc}`);
    if (!record.gameFcId) Object.assign(record, { gameFcId, gameFcName: gameFcName ?? null });

    const token = newToken();
    const hash = await sha256(token);
    const ownerKey = `owner:${checked.fc}:${owner}`;
    const previous = await this.ctx.storage.get(ownerKey);
    if (previous) await this.ctx.storage.delete(`token:${previous.tokenHash}`);

    // Quem já é membro só troca o token. A FC sem membro nenhum ganha o dono. O resto vira pedido.
    const id = await memberId(owner);
    const memberKey = `member:${checked.fc}:${id}`;
    const known = await this.ctx.storage.get(memberKey);
    const now = nowSeconds();
    const member = known?.status === 'member' ? known : {
      owner,
      name: cleanText(requester?.name) || known?.name || null,
      world: cleanText(requester?.world) || known?.world || null,
      role: (await this.memberList(checked.fc)).some((m) => m.status === 'member') ? 'member' : 'owner',
      status: 'pending',
      requestedAt: now,
      joinedAt: null,
    };
    if (member.role === 'owner' && member.status === 'pending') Object.assign(member, { status: 'member', joinedAt: now });

    await this.ctx.storage.put({
      [`fc:${checked.fc}`]: record,
      [`token:${hash}`]: { fc: checked.fc, owner, gameFcId: record.gameFcId, createdAt: now },
      [ownerKey]: { tokenHash: hash },
      [memberKey]: member,
    });
    return {
      fc: checked.fc, token, fcName: record.gameFcName, serverName: record.guildName ?? null, relinked: Boolean(previous),
      status: member.status, role: member.role,
    };
  }

  async memberList(fc) {
    const entries = await this.ctx.storage.list({ prefix: `member:${fc}:` });
    return [...entries].map(([key, value]) => ({ id: key.split(':').at(-1), ...value }));
  }

  // Quem está chamando, com a situação de membro: o plugin mostra "aguardando aprovação" e a aba de gerenciamento.
  async caller(token) {
    const entry = await this.token(token);
    if (!entry) return null;
    const member = await this.ctx.storage.get(`member:${entry.fc}:${await memberId(entry.owner)}`);
    // Ligações feitas antes dos pedidos não têm registro de membro: valem como membro.
    return { ...entry, status: member?.status ?? 'member', role: member?.role ?? 'member' };
  }

  async me(token) {
    const caller = await this.caller(token);
    if (!caller) return null;
    const record = (await this.ctx.storage.get(`fc:${caller.fc}`)) ?? {};
    const manager = MANAGERS.includes(caller.role) && caller.status === 'member';
    const pending = manager ? (await this.memberList(caller.fc)).filter((m) => m.status === 'pending').length : 0;
    return { status: caller.status, role: caller.role, fcName: record.gameFcName ?? null, pending };
  }

  // Lista para a aba de gerenciamento: só dono e admins veem.
  async members(token) {
    const caller = await this.caller(token);
    if (!caller) return { error: 'unauthorized' };
    if (!MANAGERS.includes(caller.role) || caller.status !== 'member') return { error: 'forbidden' };
    const self = await memberId(caller.owner);
    const list = (await this.memberList(caller.fc)).map((m) => ({
      id: m.id, name: m.name, world: m.world, role: m.role, status: m.status,
      requestedAt: m.requestedAt, joinedAt: m.joinedAt, you: m.id === self,
    }));
    list.sort((a, b) => (a.status === b.status ? (a.name ?? '').localeCompare(b.name ?? '') : a.status === 'pending' ? -1 : 1));
    return { members: list };
  }

  // accept, reject, kick, promote, demote. Admin aceita, recusa e expulsa membro comum; só o dono mexe em admins.
  async manage({ token, action, member }) {
    const caller = await this.caller(token);
    if (!caller) return { error: 'unauthorized' };
    if (!MANAGERS.includes(caller.role) || caller.status !== 'member') return { error: 'forbidden' };
    const key = `member:${caller.fc}:${member}`;
    const target = typeof member === 'string' ? await this.ctx.storage.get(key) : null;
    if (!target) return { error: 'unknown-member' };
    if (target.role === 'owner' || target.owner === caller.owner) return { error: 'forbidden' };
    if (target.role === 'admin' && caller.role !== 'owner') return { error: 'forbidden' };

    if (action === 'accept' && target.status === 'pending') {
      await this.ctx.storage.put(key, { ...target, status: 'member', joinedAt: nowSeconds() });
      return { ok: true };
    }
    if ((action === 'promote' || action === 'demote') && caller.role === 'owner' && target.status === 'member') {
      await this.ctx.storage.put(key, { ...target, role: action === 'promote' ? 'admin' : 'member' });
      return { ok: true };
    }
    if (action === 'reject' || action === 'kick') {
      // Sai de vez: o token para de valer e o pedido some. Pode pedir de novo com o código.
      const ownerKey = `owner:${caller.fc}:${target.owner}`;
      const link = await this.ctx.storage.get(ownerKey);
      await this.ctx.storage.delete([key, ownerKey, ...(link ? [`token:${link.tokenHash}`] : [])]);
      return { ok: true, fc: caller.fc, owner: target.owner };
    }
    return { error: 'invalid' };
  }

  async token(token) {
    if (typeof token !== 'string' || token.length > 100) return null;
    return (await this.ctx.storage.get(`token:${await sha256(token)}`)) ?? null;
  }

  // O dono desliga o plugin desta FC: o token para de valer.
  async leave(token) {
    const entry = await this.token(token);
    if (!entry) return { ok: false };
    await this.ctx.storage.delete([`token:${await sha256(token)}`, `owner:${entry.fc}:${entry.owner}`, `member:${entry.fc}:${await memberId(entry.owner)}`]);
    return { ok: true, fc: entry.fc, owner: entry.owner };
  }
}
