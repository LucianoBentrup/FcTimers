import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  accountLinked, addOwner, characterOf, charactersOf, confirmAccount, isCode, isGameFcId, isOwner, linkedOwners, newCode, newToken,
  normalizeCode, removeOwner, requestAccount, sha256, suggestedJob, ACCOUNT_CODE_TTL,
} from '../src/link.js';
import { codeScreen, parseCustomId, setupMessage } from '../src/menu.js';

const FC = '9223372036854775807';

test('o código da FC é curto, sem letras que confundem, e aceita digitação solta', () => {
  for (let n = 0; n < 200; n += 1) {
    const code = newCode();
    assert.ok(isCode(code), code);
    assert.doesNotMatch(code.slice(4), /[01ILOU]/);
  }
  assert.equal(normalizeCode('fcc 7k2q9m'), 'FCC-7K2Q9M');
  assert.equal(normalizeCode('7K2Q-9M'), 'FCC-7K2Q9M');
  assert.equal(normalizeCode('FCC-7K2Q9M'), 'FCC-7K2Q9M');
  assert.ok(!isCode(normalizeCode('FCC-7K2Q')));
  assert.ok(!isCode(normalizeCode('FCC-0O1I2L')));
});

test('ID da FC como texto (ulong) e ID do dono', () => {
  assert.ok(isGameFcId(FC));
  for (const bad of [123, '', '0', '-1', '12a', '1'.repeat(21), null]) assert.ok(!isGameFcId(bad), String(bad));
  assert.ok(isOwner('3f2c9a1e-5b7d-4e2a-9c1f-0a1b2c3d4e5f'));
  for (const bad of ['', 'curto', 'com espaço aqui', 'x'.repeat(65), 7]) assert.ok(!isOwner(bad), String(bad));
});

test('o token é aleatório e só o hash fica guardado', async () => {
  const a = newToken();
  assert.match(a, /^fco_[0-9a-f]{48}$/);
  assert.notEqual(a, newToken());
  assert.equal((await sha256(a)).length, 64);
  assert.equal(await sha256(a), await sha256(a));
});

test('só os personagens desta FC entram; os alts ficam de fora', () => {
  const characters = charactersOf([
    { name: 'Lumi Noctis', world: 'Behemoth', fcId: FC, jobs: { tank: 'GNB', healer: 'WHM', dps: 'rdm' } },
    { name: 'Kai Tarn', world: 'Behemoth', fcId: '42' },
    { name: '', fcId: FC },
    'lixo',
  ], FC);
  assert.deepEqual(characters, [{ name: 'Lumi Noctis', world: 'Behemoth', jobs: { tank: 'GNB', healer: 'WHM' } }]);
  assert.deepEqual(charactersOf(null, FC), []);
});

test('ligar a conta do Discord: código de 6 dígitos, uma vez só, dentro do prazo', () => {
  const state = {};
  addOwner(state, 'dono-0001', [{ name: 'Lumi', jobs: { healer: 'WHM' } }], 1000);
  assert.equal(linkedOwners(state), 1);
  const code = requestAccount(state, 'discord-1', 1000);
  assert.match(code, /^\d{6}$/);
  // Outro dono não liga com o código errado nem depois do prazo.
  assert.equal(confirmAccount(state, 'dono-0001', '000000', 1000), null);
  assert.equal(confirmAccount(state, 'dono-0001', code, 1000 + ACCOUNT_CODE_TTL), null);
  const fresh = requestAccount(state, 'discord-1', 2000);
  assert.equal(confirmAccount(state, 'desconhecido', fresh, 2000), null);
  assert.equal(confirmAccount(state, 'dono-0001', fresh, 2000), 'discord-1');
  assert.equal(confirmAccount(state, 'dono-0001', fresh, 2000), null);
  assert.ok(accountLinked(state, 'discord-1'));
  assert.equal(suggestedJob(state, 'discord-1', 'healer'), 'WHM');
  assert.equal(suggestedJob(state, 'discord-1', 'tank'), null);
  assert.equal(characterOf(state, 'discord-2'), null);
});

test('pedir de novo invalida o código anterior da mesma pessoa', () => {
  const state = {};
  addOwner(state, 'dono-0001', [], 0);
  const first = requestAccount(state, 'd1', 0, '111111');
  requestAccount(state, 'd1', 10, '222222');
  assert.equal(confirmAccount(state, 'dono-0001', first, 10), null);
  assert.equal(confirmAccount(state, 'dono-0001', '222222', 10), 'd1');
});

test('uma conta liga a um dono só; trocar ou sair desfaz a ligação antiga', () => {
  const state = {};
  addOwner(state, 'dono-a', [], 0);
  addOwner(state, 'dono-b', [], 0);
  confirmAccount(state, 'dono-a', requestAccount(state, 'd1', 0), 0);
  confirmAccount(state, 'dono-b', requestAccount(state, 'd1', 0), 0);
  assert.equal(state.links.owners['dono-a'].discordUser, null);
  assert.equal(state.links.discord.d1, 'dono-b');
  // Ligar o plugin de novo não perde a conta do Discord.
  addOwner(state, 'dono-b', [{ name: 'Novo' }], 50);
  assert.equal(state.links.owners['dono-b'].discordUser, 'd1');
  removeOwner(state, 'dono-b');
  assert.ok(!accountLinked(state, 'd1'));
  assert.equal(linkedOwners(state), 1);
});

test('telas do código: servidor novo, código ligado e desligado', () => {
  const setup = setupMessage();
  assert.equal(setup.flags, 64);
  assert.equal(setup.components[0].components[0].custom_id, 'fc:setup');
  assert.deepEqual(parseCustomId('fc:setup'), { kind: 'setup' });
  assert.deepEqual(parseCustomId('fc:linkme'), { kind: 'linkme' });
  const on = codeScreen('pt', 'FCC-7K2Q9M', 3);
  assert.match(on.embeds[0].description, /FCC-7K2Q9M/);
  assert.match(on.embeds[0].description, /Plugins ligados: 3/);
  const ids = on.components[0].components.map((b) => b.custom_id);
  assert.deepEqual(ids, ['fc:do:code-new', 'fc:do:code-off', 'fc:manage']);
  for (const id of ids) assert.ok(parseCustomId(id), id);
  const off = codeScreen('en', null, 0);
  assert.deepEqual(off.components[0].components.map((b) => b.label), ['Turn code on', 'Back']);
});
