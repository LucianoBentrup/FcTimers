import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import {
  COMMANDS, fcFor, langOf, LEGACY, menuComponents, menuMessage, panelComponents, parseCustomId, SECTIONS, sectionEmbed, sharedMessage, text, TEXT, userOf,
} from '../src/menu.js';
import { EPHEMERAL } from '../src/discord.js';

const buttons = (rows) => rows.flatMap((r) => r.components);
const ids = (rows) => buttons(rows).filter((b) => b.custom_id).map((b) => b.custom_id);
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
  assert.deepEqual(parseCustomId('fc:open:supplies'), { kind: 'open', view: 'supplies', arg: undefined, manager: false });
  assert.deepEqual(parseCustomId('fc:pref:dm'), { kind: 'pref', name: 'dm' });
  assert.deepEqual(parseCustomId('fc:set:eventrole'), { kind: 'eventrole' });
});

test('custom_id dos eventos', () => {
  assert.deepEqual(parseCustomId('fc:evnew'), { kind: 'evnew', type: null });
  assert.deepEqual(parseCustomId('fc:evnew:group'), { kind: 'evnew', type: 'group' });
  assert.deepEqual(parseCustomId('fc:evm:gathering'), { kind: 'evmodal', type: 'gathering' });
  assert.deepEqual(parseCustomId('fc:evd:size:24'), { kind: 'evdraft', op: 'size', value: '24' });
  assert.deepEqual(parseCustomId('fc:ev:ab12cd34ef:tank'), { kind: 'signup', id: 'ab12cd34ef', choice: 'tank' });
  assert.deepEqual(parseCustomId('fc:evx:ab12cd34ef:cancel'), { kind: 'evcancel', id: 'ab12cd34ef' });
  for (const id of ['fc:evnew:raid', 'fc:evm:x', 'fc:evd:apagar', 'fc:ev:ab12:chef', 'fc:ev:AB-12:tank', 'fc:evx:ab12:delete', 'fc:ev:ab:tank:extra']) {
    assert.equal(parseCustomId(id), null, id);
  }
});

test('custom_id desconhecido, de outro app ou de botão que não existe mais é recusado', () => {
  for (const id of ['', 'x:subs', 'fc', 'fc:nada', 'fc:do:apagar-tudo', 'fc:share:manage', 'fc:share:panel', 'fc:share:events', 'fc:pref:x', 'fc:set:x', `fc:${'a'.repeat(120)}`, null, 42]) {
    assert.equal(parseCustomId(id), null, String(id));
  }
});

test('todo botão do menu leva a algo que o parser entende', () => {
  for (const lang of ['en', 'pt']) {
    for (const view of allViews) {
      for (const weeklyOff of [false, true]) {
        const rows = menuComponents(lang, {
          view, arg: view === 'loot' ? 'week' : undefined, manager: true, weeklyOff, canCreate: true, dmOn: weeklyOff,
        });
        for (const id of ids(rows)) assert.ok(parseCustomId(id), `${lang} ${view}: ${id}`);
      }
    }
  }
});

test('o menu respeita os limites do Discord', () => {
  for (const view of allViews) {
    const rows = menuComponents('pt', { view, arg: 'day', manager: true, canCreate: true, eventRole: '9' });
    assert.ok(rows.length <= 5, view);
    for (const r of rows) {
      assert.ok(r.components.length >= 1 && r.components.length <= 5, view);
      for (const b of r.components) {
        if (b.type === 2) assert.ok(b.label.length <= 80);
        assert.ok(b.custom_id.length <= 100);
      }
      // Lista de cargos ocupa a linha sozinha.
      if (r.components.some((b) => b.type !== 2)) assert.equal(r.components.length, 1);
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

test('Eventos: Novo evento só para quem pode criar; o lembrete por DM é de cada um', () => {
  const member = ids(menuComponents('pt', { view: 'events', canCreate: false, dmOn: false }));
  const creator = buttons(menuComponents('pt', { view: 'events', canCreate: true, dmOn: true }));
  assert.ok(!member.includes('fc:evnew'));
  assert.ok(member.includes('fc:pref:dm'));
  assert.ok(creator.some((b) => b.custom_id === 'fc:evnew'));
  assert.equal(creator.find((b) => b.custom_id === 'fc:pref:dm').label, 'Lembrete por DM: ligado');
});

test('os ajustes têm os canais do bot e a lista de cargos com o cargo atual', () => {
  const rows = menuComponents('en', { view: 'manage', manager: true, eventRole: '77' });
  assert.ok(['fc:do:panel-here', 'fc:do:alerts-here', 'fc:do:events-here'].every((id) => ids(rows).includes(id)));
  const select = buttons(rows).find((b) => b.custom_id === 'fc:set:eventrole');
  assert.equal(select.type, 6);
  assert.deepEqual(select.default_values, [{ id: '77', type: 'role' }]);
});

test('botões do painel abrem o menu já na seção', () => {
  const rows = panelComponents('en');
  assert.deepEqual(ids(rows), ['fc:open:loot:day', 'fc:open:supplies', 'fc:open:events']);
  for (const id of ids(rows)) assert.equal(parseCustomId(id).kind, 'open');
  assert.equal(buttons(panelComponents('pt'))[0].label, 'Loot de hoje');
});

test('Ko-fi: botão de link só com a URL preenchida, sem custom_id', () => {
  assert.ok(!buttons(menuComponents('en', { view: 'panel' })).some((b) => b.url));
  const link = buttons(menuComponents('pt', { view: 'panel', manager: true, supportUrl: 'https://ko-fi.com/x' })).find((b) => b.url);
  assert.equal(link.style, 5);
  assert.equal(link.label, 'Ko-fi');
  assert.equal(link.custom_id, undefined);
  // Cabe na última linha mesmo com Eventos, Postar no canal e Ajustes.
  const rows = menuComponents('en', { view: 'subs', manager: true, supportUrl: 'https://ko-fi.com/x' });
  assert.ok(rows.every((r) => r.components.length <= 5));
  // Em todas as seções e embaixo do painel.
  for (const view of allViews) assert.ok(buttons(menuComponents('en', { view, supportUrl: 'https://ko-fi.com/x' })).some((b) => b.url), view);
  assert.equal(buttons(panelComponents('en', 'https://ko-fi.com/x')).at(-1).url, 'https://ko-fi.com/x');
  assert.ok(!buttons(panelComponents('en', '')).some((b) => b.url));
});

test('o menu do canal é de todos, o privado é só de quem abriu, e nenhum marca ninguém', () => {
  const message = menuMessage('pt', { embed: sectionEmbed('T', 'x'), view: 'subs', manager: false, notice: 'oi' });
  assert.equal(message.flags, EPHEMERAL);
  assert.deepEqual(message.allowed_mentions, { parse: [] });
  assert.equal(message.content, 'oi');
  assert.equal(message.embeds[0].footer.text, 'Só você vê este menu.');
  const pub = menuMessage('pt', { embed: sectionEmbed('Subs', 'x'), view: 'subs', manager: false, pub: true });
  assert.equal(pub.flags, undefined);
  assert.match(pub.embeds[0].footer.text, /Todo mundo aqui/);
  const pubIds = pub.components.flatMap((r) => r.components).map((b) => b.custom_id);
  assert.ok(pubIds.includes('fc:manage'));
  assert.ok(!pubIds.some((id) => id.startsWith('fc:share')));
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
