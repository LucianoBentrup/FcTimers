import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import {
  COMMANDS, fcFor, langOf, LEGACY, menuComponents, menuMessage, parseCustomId, SECTIONS, sectionEmbed, sharedMessage, text, TEXT, userOf,
} from '../src/menu.js';
import { EPHEMERAL } from '../src/discord.js';

const buttons = (rows) => rows.flatMap((r) => r.components);
const ids = (rows) => buttons(rows).map((b) => b.custom_id);
const allViews = [...SECTIONS.map((s) => s.id), 'manage'];

test('o Discord vê um comando só, /fcc, e o comandos.json está em dia', () => {
  assert.deepEqual(COMMANDS.map((c) => c.name), ['fcc']);
  const file = JSON.parse(readFileSync(new URL('../../tools/comandos.json', import.meta.url), 'utf8'));
  assert.deepEqual(file, COMMANDS, 'rode `npm run commands` para gerar o tools/comandos.json de novo');
});

test('todo texto do menu existe em inglês e em português', () => {
  assert.deepEqual(Object.keys(TEXT.en).sort(), Object.keys(TEXT.pt).sort());
  for (const lang of ['en', 'pt']) for (const [key, value] of Object.entries(TEXT[lang])) assert.ok(value, `${lang}.${key}`);
  assert.equal(text('en', 'supplies'), 'Supplies');
  assert.equal(text('pt', 'supplies'), 'Estoque');
  // Idioma desconhecido cai no inglês.
  assert.equal(text('jp', 'supplies'), 'Supplies');
});

test('a FC atual continua em português, e o ajuste troca o idioma', () => {
  assert.equal(langOf({}), 'pt');
  assert.equal(langOf({ settings: { lang: 'en' } }), 'en');
  assert.equal(langOf({ settings: { lang: 'xx' } }), 'pt');
});

test('custom_id: seções, períodos do loot, ações e compartilhar', () => {
  assert.deepEqual(parseCustomId('fc:subs'), { kind: 'view', view: 'subs', arg: undefined, manager: false });
  assert.deepEqual(parseCustomId('fc:loot'), { kind: 'view', view: 'loot', arg: 'day', manager: false });
  assert.equal(parseCustomId('fc:loot:week').arg, 'week');
  assert.equal(parseCustomId('fc:loot:ano').arg, 'day');
  assert.deepEqual(parseCustomId('fc:manage'), { kind: 'view', view: 'manage', arg: undefined, manager: true });
  assert.deepEqual(parseCustomId('fc:do:repost'), { kind: 'action', name: 'repost' });
  assert.equal(parseCustomId('fc:share:loot:month').kind, 'share');
  assert.equal(parseCustomId('fc:share:loot:month').arg, 'month');
});

test('custom_id desconhecido, de outro app ou de botão que não existe mais é recusado', () => {
  for (const id of ['', 'x:subs', 'fc', 'fc:nada', 'fc:do:apagar-tudo', 'fc:share:manage', 'fc:share:panel', `fc:${'a'.repeat(120)}`, null, 42]) {
    assert.equal(parseCustomId(id), null, String(id));
  }
});

test('todo botão do menu leva a algo que o parser entende', () => {
  for (const lang of ['en', 'pt']) {
    for (const view of allViews) {
      for (const weeklyOff of [false, true]) {
        const rows = menuComponents(lang, { view, arg: view === 'loot' ? 'week' : undefined, manager: true, weeklyOff });
        for (const id of ids(rows)) assert.ok(parseCustomId(id), `${lang} ${view}: ${id}`);
      }
    }
  }
});

test('o menu respeita os limites do Discord', () => {
  for (const view of allViews) {
    const rows = menuComponents('pt', { view, arg: 'day', manager: true });
    assert.ok(rows.length <= 5, view);
    for (const r of rows) {
      assert.ok(r.components.length >= 1 && r.components.length <= 5, view);
      for (const b of r.components) {
        assert.ok(b.label.length <= 80);
        assert.ok(b.custom_id.length <= 100);
      }
    }
    assert.equal(new Set(ids(rows)).size, ids(rows).length, `${view}: custom_id repetido`);
  }
});

test('a seção aberta fica em destaque, e o loot mostra o período escolhido', () => {
  const rows = menuComponents('en', { view: 'loot', arg: 'week', manager: false });
  const style = (id) => buttons(rows).find((b) => b.custom_id === id).style;
  assert.equal(style('fc:loot'), 1);
  assert.equal(style('fc:subs'), 2);
  assert.equal(style('fc:loot:week'), 1);
  assert.equal(style('fc:loot:day'), 2);
  assert.ok(ids(rows).includes('fc:share:loot:week'));
});

test('membro comum não vê os ajustes; quem gerencia vê', () => {
  const member = ids(menuComponents('pt', { view: 'panel', manager: false }));
  const manager = ids(menuComponents('pt', { view: 'panel', manager: true }));
  assert.ok(!member.includes('fc:manage'));
  assert.ok(manager.includes('fc:manage'));
  assert.ok(!member.some((id) => id.startsWith('fc:do:')));
});

test('os ajustes mostram o resumo semanal como está e o botão faz o contrário', () => {
  const on = buttons(menuComponents('pt', { view: 'manage', manager: true, weeklyOff: false }));
  const off = buttons(menuComponents('pt', { view: 'manage', manager: true, weeklyOff: true }));
  assert.equal(on.find((b) => b.custom_id === 'fc:do:weekly-off').label, 'Resumo semanal: ligado');
  assert.equal(off.find((b) => b.custom_id === 'fc:do:weekly-on').label, 'Resumo semanal: desligado');
  assert.ok(buttons(menuComponents('pt', { view: 'manage', manager: true })).some((b) => b.custom_id === 'fc:do:lang-en'));
  assert.ok(buttons(menuComponents('en', { view: 'manage', manager: true })).some((b) => b.custom_id === 'fc:do:lang-pt'));
});

test('o menu é só de quem chamou e nunca marca ninguém', () => {
  const message = menuMessage('pt', { embed: sectionEmbed('T', 'x'), view: 'subs', manager: false, notice: 'oi' });
  assert.equal(message.flags, EPHEMERAL);
  assert.deepEqual(message.allowed_mentions, { parse: [] });
  assert.equal(message.content, 'oi');
  assert.equal(message.embeds[0].footer.text, 'Só você vê este menu.');
});

test('postar no canal: sem botões, sem marcar, com quem postou', () => {
  const message = sharedMessage('en', sectionEmbed('T', 'x'), '123');
  assert.equal(message.flags, undefined);
  assert.equal(message.components, undefined);
  assert.deepEqual(message.allowed_mentions, { parse: [] });
  assert.match(message.content, /<@123>/);
});

test('texto longo é cortado no limite do embed', () => {
  const embed = sectionEmbed('T', 'a'.repeat(5000));
  assert.ok(embed.description.length <= 4096);
  assert.ok(embed.description.endsWith('…'));
  assert.equal(sectionEmbed('T', '').description, '—');
});

test('cada servidor só vê a própria FC', () => {
  const env = { DISCORD_GUILD_ID: '42' };
  assert.equal(fcFor({ guild_id: '42' }, env), 'fc');
  assert.equal(fcFor({ guild_id: '1' }, env), null);
  assert.equal(fcFor({}, env), null);
});

test('os comandos antigos abrem a seção equivalente', () => {
  assert.equal(LEGACY.subs.view, 'subs');
  assert.equal(LEGACY.estoque.view, 'supplies');
  assert.equal(LEGACY.loot.args.semana, 'week');
  for (const legacy of Object.values(LEGACY)) assert.ok(allViews.includes(legacy.view));
});

test('quem clicou vem do membro no servidor ou do usuário fora dele', () => {
  assert.equal(userOf({ member: { user: { id: '1' } } }), '1');
  assert.equal(userOf({ user: { id: '2' } }), '2');
  assert.equal(userOf({}), null);
});
