// Ligação plugin ↔ FC ↔ conta do Discord.
// Código da FC (FCC-XXXXXX) gerado no /fcc; o plugin manda código + ID da FC do jogo + dono (instalação).
// A primeira ligação fixa o ID da FC do jogo. Ligação é por dono, não por personagem; alt de outra FC não entra.
// Conta do Discord é opcional: código de 6 dígitos no /fcc, digitado no jogo.

export const CODE_PREFIX = 'FCC-';
// Sem 0/O, 1/I/L e U: dá para ditar o código sem confusão. 30^6 ≈ 729 milhões de códigos.
const CODE_ALPHABET = '23456789ABCDEFGHJKMNPQRSTVWXYZ';
const CODE_LENGTH = 6;
// Tentativas erradas de código por dono e por IP, a cada hora.
export const CODE_ATTEMPTS = 10;
export const CODE_WINDOW = 3600;
// Código para ligar a conta do Discord: 6 dígitos, vale 15 minutos.
export const ACCOUNT_CODE_TTL = 900;
const MAX_CHARACTERS = 20;
const MAX_NAME = 40;

const randomIndex = (max) => {
  const buffer = new Uint32Array(1);
  crypto.getRandomValues(buffer);
  return buffer[0] % max;
};

export const newCode = () => CODE_PREFIX + Array.from({ length: CODE_LENGTH }, () => CODE_ALPHABET[randomIndex(CODE_ALPHABET.length)]).join('');

// "fcc 7k2q9m", "7K2Q-9M" e "FCC-7K2Q9M" valem o mesmo código.
export function normalizeCode(value) {
  const raw = String(value ?? '').toUpperCase().replace(/[^0-9A-Z]/g, '');
  const body = raw.startsWith('FCC') && raw.length === CODE_LENGTH + 3 ? raw.slice(3) : raw;
  return `${CODE_PREFIX}${body}`;
}
export const isCode = (value) => new RegExp(`^${CODE_PREFIX}[${CODE_ALPHABET}]{${CODE_LENGTH}}$`).test(value);

// O token que o plugin guarda no lugar da chave da FC. Só o hash fica no serviço.
export function newToken() {
  const bytes = new Uint8Array(24);
  crypto.getRandomValues(bytes);
  return `fco_${[...bytes].map((b) => b.toString(16).padStart(2, '0')).join('')}`;
}
export async function sha256(value) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

// O ID da FC é um ulong: vai como texto, porque não cabe num número do JavaScript.
export const isGameFcId = (value) => typeof value === 'string' && /^[1-9]\d{0,19}$/.test(value);
// O ID do dono é gerado pelo plugin na instalação (um GUID, por exemplo).
export const isOwner = (value) => typeof value === 'string' && /^[A-Za-z0-9-]{8,64}$/.test(value);

export const newAccountCode = () => String(100000 + randomIndex(900000));

const cleanName = (value) => (typeof value === 'string' ? value.replace(/[\u0000-\u001f]/g, '').trim().slice(0, MAX_NAME) : '');

// Personagens que o plugin manda ao ligar. Só os desta FC ficam; os outros (alts) nem são guardados.
export function charactersOf(list, gameFcId) {
  if (!Array.isArray(list)) return [];
  return list
    .filter((c) => c && c.fcId === gameFcId && cleanName(c.name))
    .slice(0, MAX_CHARACTERS)
    .map((c) => ({ name: cleanName(c.name), world: cleanName(c.world) || null, jobs: jobsOf(c.jobs) }));
}

const JOB = /^[A-Z]{3}$/;
function jobsOf(jobs) {
  if (!jobs || typeof jobs !== 'object') return {};
  return Object.fromEntries(['tank', 'healer', 'dps'].filter((r) => JOB.test(jobs[r] ?? '')).map((r) => [r, jobs[r]]));
}

// ---------- no estado da FC ----------
// state.links = {
//   owners:  { <dono>: { linkedAt, characters: [{ name, world, jobs }], discordUser } },
//   discord: { <id do Discord>: <dono> },
//   pending: { <código de 6 dígitos>: { user, expires } },
// }

const linksOf = (state) => {
  state.links ??= { owners: {}, discord: {}, pending: {} };
  state.links.owners ??= {};
  state.links.discord ??= {};
  state.links.pending ??= {};
  return state.links;
};

export function addOwner(state, owner, characters, now) {
  const links = linksOf(state);
  const previous = links.owners[owner];
  links.owners[owner] = { linkedAt: previous?.linkedAt ?? now, characters, discordUser: previous?.discordUser ?? null };
  return links.owners[owner];
}

export function removeOwner(state, owner) {
  const links = linksOf(state);
  const user = links.owners[owner]?.discordUser;
  if (user) delete links.discord[user];
  delete links.owners[owner];
}

// O /fcc pediu para ligar a conta: código novo, e o antigo dessa pessoa deixa de valer.
export function requestAccount(state, user, now, code = newAccountCode()) {
  const links = linksOf(state);
  for (const [key, entry] of Object.entries(links.pending)) if (entry.user === user || entry.expires <= now) delete links.pending[key];
  while (links.pending[code]) code = newAccountCode();
  links.pending[code] = { user, expires: now + ACCOUNT_CODE_TTL };
  return code;
}

// O dono digitou o código no jogo. Devolve o id do Discord ligado, ou null.
export function confirmAccount(state, owner, code, now) {
  const links = linksOf(state);
  const entry = links.pending[String(code ?? '').trim()];
  if (!entry || entry.expires <= now || !links.owners[owner]) return null;
  delete links.pending[String(code).trim()];
  // Uma conta do Discord liga a um dono só, e um dono a uma conta só.
  const before = links.discord[entry.user];
  if (before && links.owners[before]) links.owners[before].discordUser = null;
  const old = links.owners[owner].discordUser;
  if (old) delete links.discord[old];
  links.owners[owner].discordUser = entry.user;
  links.discord[entry.user] = owner;
  return entry.user;
}

export const linkedOwners = (state) => Object.keys(state.links?.owners ?? {}).length;
export const accountLinked = (state, discordUserId) => Boolean(state.links?.discord?.[discordUserId]);

// Personagem ligado a esta conta do Discord, ou null: o primeiro personagem desta FC do dono.
export function characterOf(state, discordUserId) {
  const owner = state?.links?.discord?.[discordUserId];
  return owner ? state.links.owners[owner]?.characters?.[0] ?? null : null;
}

// Job sugerido para a função ('tank', 'healer', 'dps'), ou null.
export function suggestedJob(state, discordUserId, role) {
  return characterOf(state, discordUserId)?.jobs?.[role] ?? null;
}
