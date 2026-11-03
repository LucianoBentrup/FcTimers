import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  alertDeleteAt, alertPayload, applyUpdate, dayOf, isValidUpdate, markSent, mergeVoyage, needsPrice, newState, nextEvent, nextMidnight,
  parseUniversalis, pendingAlerts, priceVoyage, publicState, recentDays, renderPanel, sectorNotice, voyageKey,
} from '../src/logic.js';

const at = (day, hour, minute = 0) => Math.floor(Date.UTC(2026, 10, day, hour + 3, minute) / 1000);
const voyage = (slot, returnTime, value, itemId = 1) => ({
  slot, subName: `Sub${slot}`, returnTime, items: [{ itemId, name: `Item${itemId}`, count: 1, value }],
});

test('a 5ª posição enviada por versões antigas é ignorada e sai do estado', () => {
  const state = newState();
  state.subs[4] = { name: 'Navi', returnTime: at(1, 8), observedAt: at(1, 7), notified: 0 };
  const sub = (slot) => ({ slot, name: 'Navi', returnTime: at(1, 8), age: 0 });
  applyUpdate(state, { subs: [sub(0), sub(4)] }, at(1, 7));
  assert.deepEqual(Object.keys(state.subs), ['0']);
  assert.ok(isValidUpdate({ subs: [sub(4)] }));
});

test('o dia vira à meia-noite de Brasília, que é meio-dia no Japão', () => {
  assert.equal(dayOf(at(1, 23, 59)), '2026-11-01');
  assert.equal(dayOf(at(2, 0)), '2026-11-02');
  assert.equal(nextMidnight(at(1, 10, 7)), at(2, 0));
});

test('viagens de dias diferentes ficam em chaves de dias diferentes', () => {
  assert.notEqual(voyageKey(voyage(0, at(1, 23, 59), 1)), voyageKey(voyage(0, at(2, 0), 1)));
  assert.ok(voyageKey(voyage(0, at(1, 8), 1)).startsWith('voyage:2026-11-01:'));
});

test('2 pessoas enviando a mesma viagem geram um registro só', () => {
  const first = mergeVoyage(undefined, voyage(0, at(1, 8), 100));
  assert.equal(mergeVoyage(first, voyage(0, at(1, 8), 100)), null);
});

test('leitura com menos itens não substitui a viagem', () => {
  const full = { ...voyage(0, at(1, 8), 100) };
  full.items = [...full.items, { itemId: 2, name: 'Item2', count: 1, value: 5 }];
  assert.equal(mergeVoyage(full, voyage(0, at(1, 8), 100)), null);
});

test('leitura corrigida com a mesma quantidade de itens substitui', () => {
  assert.equal(mergeVoyage(voyage(0, at(1, 8), 100), voyage(0, at(1, 8), 150)).items[0].value, 150);
});

test('o total do dia acumula entre coletas e aparece por submarino', () => {
  const state = newState();
  const today = [];
  const totals = [];
  for (const slot of [0, 1, 2, 3]) {
    today.push(voyage(slot, at(1, 8 + slot), 100));
    const panel = renderPanel(state, today, at(1, 15));
    totals.push(panel.embeds[0].fields[3].value.split('\n')[0]);
  }
  assert.deepEqual(totals, ['**100 gil**', '**200 gil**', '**300 gil**', '**400 gil**']);
});

test('segunda rodada soma no valor do submarino', () => {
  const panel = renderPanel(newState(), [voyage(0, at(1, 8), 100), voyage(0, at(1, 20), 50)], at(1, 21));
  assert.match(panel.embeds[0].fields[3].value, /\*\*Sub0\*\*: 150 gil/);
});

test('leitura antiga de outro membro não volta o status do submarino', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 5000, age: 0 }] }, 1000);
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 3000, age: 300 }] }, 1100);
  assert.equal(state.subs[0].returnTime, 5000);
});

test('leitura mais recente atualiza o status do submarino', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 5000, age: 0 }] }, 1000);
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 9000, age: 0 }] }, 1100);
  assert.equal(state.subs[0].returnTime, 9000);
});

test('a hora do buff usa o relógio do servidor e não muda com as mesmas horas', () => {
  const state = newState();
  const buffs = { age: 0, slots: [{ slot: 5, itemId: 9669, name: 'Roda', hours: 70 }] };
  applyUpdate(state, { buffs }, 1000);
  const ready = state.buffs[5].readyTime;
  assert.equal(ready, 1000 + (70 * 3600));
  applyUpdate(state, { buffs }, 1500);
  assert.equal(state.buffs[5].readyTime, ready);
  applyUpdate(state, { buffs: { age: 0, slots: [{ slot: 5, itemId: 9669, name: 'Roda', hours: 69 }] } }, 2000);
  assert.equal(state.buffs[5].readyTime, 2000 + (69 * 3600));
});

test('buff retirado sai do painel, e leitura antiga não o traz de volta', () => {
  const state = newState();
  applyUpdate(state, { buffs: { age: 0, slots: [{ slot: 5, itemId: 9669, name: 'Roda', hours: 70 }] } }, 1000);
  applyUpdate(state, { buffs: { age: 0, slots: [] } }, 2000);
  applyUpdate(state, { buffs: { age: 1500, slots: [{ slot: 5, itemId: 9669, name: 'Roda', hours: 70 }] } }, 2100);
  assert.deepEqual(state.buffs, {});
});

test('o painel mostra spoiler no fim e nenhum texto de teste', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: at(1, 18), age: 0 }] }, at(1, 15));
  const embed = renderPanel(state, [], at(1, 15)).embeds[0];
  assert.equal(embed.title, 'Timers');
  assert.match(embed.fields[0].value, /volta <t:\d+:R>/);
  assert.match(embed.fields.at(-1).value, /\|\|Atualizado <t:\d+:R>\|\|$/);
  assert.doesNotMatch(JSON.stringify(embed), /teste|cerca de/i);
});

const activeBuffs = (buffs, age = 0) => ({ activeBuffs: { age, buffs } });

test('buffs ativos ficam depois dos itens, com nível e tempo', () => {
  const state = newState();
  applyUpdate(state, activeBuffs([{ name: 'Meat and Mead II', seconds: 86392 }, { name: 'The Heat of Battle II', seconds: 86394 }]), at(1, 15));
  assert.equal(renderPanel(state, [], at(1, 15), true).embeds[0].fields.at(-2).name, '📦 Itens de hoje');
  const { fields } = renderPanel(state, [], at(1, 15)).embeds[0];
  assert.ok(fields.every((f) => f.name !== '📦 Itens de hoje'));
  assert.equal(fields.at(-1).name, '✨ Buffs ativos');
  assert.match(fields.at(-1).value, /\*\*Meat and Mead II\*\*: <t:\d+:R>\n\*\*The Heat of Battle II\*\*: <t:\d+:R>/);
  assert.doesNotMatch(fields.at(-1).value, /Lido/);
});

test('buff sem tempo aparece com ? e o horário da leitura', () => {
  const state = newState();
  applyUpdate(state, activeBuffs([{ name: 'Seal Sweetener', seconds: null }]), at(1, 15));
  assert.match(renderPanel(state, [], at(1, 15)).embeds[0].fields.at(-1).value, /\*\*Seal Sweetener\*\*: \?\nLido <t:\d+:R>/);
});

test('buff que acabou sai do painel mesmo sem nova leitura', () => {
  const state = newState();
  applyUpdate(state, activeBuffs([{ name: 'Meat and Mead II', seconds: 3600 }]), at(1, 15));
  assert.equal(nextEvent(state, at(1, 15)), at(1, 16));
  assert.match(renderPanel(state, [], at(1, 17)).embeds[0].fields.at(-1).value, /^Nenhum\n/);
});

test('releituras do mesmo buff não mudam o horário', () => {
  const state = newState();
  applyUpdate(state, activeBuffs([{ name: 'Meat and Mead II', seconds: 3600 }]), 1000);
  const end = state.activeBuffs.buffs[0].endTime;
  applyUpdate(state, activeBuffs([{ name: 'Meat and Mead II', seconds: 1800 + 30 }]), 2800);
  assert.equal(state.activeBuffs.buffs[0].endTime, end);
});

test('leitura sem tempo não apaga o horário lido por outro membro', () => {
  const state = newState();
  applyUpdate(state, activeBuffs([{ name: 'Meat and Mead II', seconds: 3600 }]), 1000);
  const end = state.activeBuffs.buffs[0].endTime;
  applyUpdate(state, activeBuffs([{ name: 'Meat and Mead II', seconds: null }]), 2000);
  assert.equal(state.activeBuffs.buffs[0].endTime, end);
});

test('horário já vencido não volta em uma nova ativação sem tempo', () => {
  const state = newState();
  applyUpdate(state, activeBuffs([{ name: 'Meat and Mead II', seconds: 100 }]), 1000);
  applyUpdate(state, activeBuffs([{ name: 'Meat and Mead II', seconds: null }]), 2000);
  assert.equal(state.activeBuffs.buffs[0].endTime, null);
});

test('leitura antiga não traz de volta um buff que acabou', () => {
  const state = newState();
  applyUpdate(state, activeBuffs([]), 2000);
  applyUpdate(state, activeBuffs([{ name: 'Seal Sweetener', seconds: null }], 500), 2100);
  assert.deepEqual(state.activeBuffs.buffs, []);
  assert.match(renderPanel(state, [], 2100).embeds[0].fields.at(-1).value, /^Nenhum\n/);
});

test('sem leitura, os buffs ativos dizem que não há dados', () => {
  assert.match(renderPanel(newState(), [], at(1, 15)).embeds[0].fields.at(-1).value, /Nenhum dado ainda/);
});

test('o painel respeita o limite de texto do Discord', () => {
  const items = Array.from({ length: 16 }, (_, i) => ({ itemId: i + 1, name: 'x'.repeat(128), count: 99, value: 1000000 }));
  const panel = renderPanel(newState(), Array.from({ length: 8 }, (_, i) => ({ slot: i % 4, subName: 'x'.repeat(128), returnTime: at(1, 8), items })), at(1, 9), true);
  for (const field of panel.embeds[0].fields) assert.ok(field.value.length <= 1024, `${field.name}: ${field.value.length}`);
});

test('o próximo evento é o lembrete, o retorno ou a meia-noite', () => {
  const state = newState();
  assert.equal(nextEvent(state, at(1, 10)), at(2, 0));
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: at(1, 12), age: 0 }] }, at(1, 10));
  assert.equal(nextEvent(state, at(1, 10)), at(1, 11));
  assert.equal(nextEvent(state, at(1, 11)), at(1, 12));
});

const pendingReturns = (state, now) => pendingAlerts(state, now).find((a) => a.kind === 'subReady')?.subs ?? [];

test('submarino em viagem gera aviso ao voltar, uma vez só', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 5000, age: 0 }] }, 1000);
  assert.equal(pendingReturns(state, 4999).length, 0);
  assert.equal(pendingReturns(state, 5000).length, 1);
  state.subs[0].notified = 5000;
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 5000, age: 0 }] }, 6000);
  assert.equal(pendingReturns(state, 6000).length, 0);
});

test('submarino visto já de volta não gera aviso atrasado', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 500, age: 0 }] }, 1000);
  assert.equal(pendingReturns(state, 1000).length, 0);
});

test('submarino parado não gera aviso', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 0, age: 0 }] }, 1000);
  assert.equal(pendingReturns(state, 2000).length, 0);
});

test('reenvio gera novo aviso na volta seguinte', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 5000, age: 0 }] }, 1000);
  state.subs[0].notified = 5000;
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 9000, age: 0 }] }, 6000);
  assert.equal(pendingReturns(state, 9000).length, 1);
});

test('o aviso marca só o cargo e junta os nomes', () => {
  const one = alertPayload({ kind: 'subReady', subs: [{ name: 'Navi' }] }, '123');
  assert.equal(one.content, '<@&123> 🚢 **Navi** voltou!');
  assert.deepEqual(one.allowed_mentions, { parse: [], roles: ['123'] });
  const three = alertPayload({ kind: 'subReady', subs: [{ name: 'Navi' }, { name: 'Tatl' }, { name: 'Tael' }] }, '123');
  assert.equal(three.content, '<@&123> 🚢 **Navi**, **Tatl** e **Tael** voltaram!');
});

test('todo aviso se apaga 8 horas depois de enviado', () => {
  assert.equal(alertDeleteAt(1000), 1000 + (8 * 3600));
});

const subsAt = (...returns) => ({ subs: returns.map((returnTime, slot) => ({ slot, name: `Sub${slot}`, returnTime, age: 0 })) });

test('lembrete 1 hora antes junta quem volta perto e sai uma vez só', () => {
  const state = newState();
  applyUpdate(state, subsAt(at(1, 14, 58), at(1, 14, 58), at(1, 15, 9), at(1, 20)), at(1, 6));
  assert.equal(nextEvent(state, at(1, 6)), at(1, 13, 58));
  assert.equal(pendingAlerts(state, at(1, 13, 57)).length, 0);
  const [reminder] = pendingAlerts(state, at(1, 13, 58));
  assert.equal(reminder.kind, 'subReminder');
  assert.deepEqual(reminder.subs.map((s) => s.name), ['Sub0', 'Sub1', 'Sub2']);
  assert.equal(alertPayload(reminder, '9').content,
    '<@&9> Lembrete: Em 1 hora os submarinos estarão prontos para coleta (**Sub0**, **Sub1** e **Sub2**).');
  markSent(state, reminder);
  assert.equal(pendingAlerts(state, at(1, 14, 9)).length, 0);
});

test('sem tempo para o lembrete, só sai o aviso de pronto', () => {
  const state = newState();
  applyUpdate(state, subsAt(at(1, 10, 20)), at(1, 10));
  assert.equal(pendingAlerts(state, at(1, 10)).length, 0);
  assert.equal(pendingAlerts(state, at(1, 10, 20))[0].kind, 'subReady');
});

// Vasos de dentro de casa, que aparecem com nome nos avisos.
const plot = (key, name, nextWaterAt, dieAt = 0, age = 0) => ({ key, name, nextWaterAt, dieAt, age, indoor: true });

test('detalhes dos subs: rank no nome, build e rota no topo, desgaste e aviso de reparo', () => {
  const state = newState();
  const details = (condition) => ({ rank: 130, exp: 51, build: 'S+S+U+C+', route: 'J > O > R > Z', tanks: 21, kits: 24, condition });
  const subs = [0, 1, 2, 3].map((slot) => ({ slot, name: `Sub${slot}`, returnTime: at(2, 15), age: 0, details: details(40.4) }));
  assert.equal(isValidUpdate({ subs }), true);
  assert.equal(isValidUpdate({ subs: [{ ...subs[0], details: { ...details(40), exp: 120 } }] }), false);
  applyUpdate(state, { subs, supplies: { tanks: 579, kits: 65, age: 0 } }, at(1, 9));

  const fields = renderPanel(state, [], at(1, 9)).embeds[0].fields;
  const subField = fields[0].value.split('\n');
  assert.equal(subField[0], '*S+S+U+C+ · J > O > R > Z*');
  assert.equal(subField[1], `**Sub0** (R130): volta <t:${at(2, 15)}:R> (<t:${at(2, 15)}:t>) · 🔧 40%`);
  assert.match(fields.find((f) => f.name.includes('Estoque')).value, /579\*\* tanques de ceruleum \(≈ 6 rodadas\)\n\*\*65\*\* kits de reparo \(≈ 2 reparos completos\)/);
  assert.deepEqual(pendingAlerts(state, at(1, 9)), []);

  // Abaixo de 30%, um aviso só com os subs gastos, e não repete.
  applyUpdate(state, { subs: subs.map((s) => ({ ...s, details: details(s.slot < 2 ? 28.9 : 40) })) }, at(1, 10));
  const [repair] = pendingAlerts(state, at(1, 10));
  assert.equal(alertPayload(repair, '').content,
    '🔧 Hora de reparar: **Sub0** (28%) e **Sub1** (28%). O reparo completo gasta 48 kits, 65 no estoque.');
  markSent(state, repair);
  assert.deepEqual(pendingAlerts(state, at(1, 11)), []);
  assert.match(renderPanel(state, [], at(1, 11)).embeds[0].fields[0].value, /\*\*Sub0\*\* \(R130\): .* · ⚠️ 28%/);

  // Depois do reparo, uma nova queda volta a avisar.
  applyUpdate(state, { subs: [{ ...subs[0], details: details(100) }] }, at(1, 12));
  applyUpdate(state, { subs: [{ ...subs[0], details: details(25) }] }, at(1, 13));
  assert.equal(pendingAlerts(state, at(1, 13))[0].subs.map((s) => s.name).join(), 'Sub0');

  // Plugin antigo sem detalhes não apaga o que já se sabe.
  applyUpdate(state, { subs: [{ slot: 2, name: 'Sub2', returnTime: at(2, 15), age: 0 }] }, at(1, 14));
  assert.equal(state.subs[2].details.rank, 130);
});

test('market: preço do Universalis, maior valor no total e aviso de item caro', () => {
  assert.deepEqual(parseUniversalis({ items: { 5: { itemID: 5, averagePriceNQ: 20000, minPrice: 25000 }, 6: { itemID: 6, averagePriceNQ: 0, minPrice: 900 } } }), { 5: 20000, 6: 900 });
  assert.deepEqual(parseUniversalis({ itemID: 7, averagePriceNQ: 0, averagePrice: 0, minPrice: 0 }), {});

  const incoming = {
    slot: 2, subName: 'Tatl', returnTime: at(1, 6), world: 'Behemoth', unlocked: ['Deep-sea Site 9'],
    items: [
      { itemId: 5, name: 'Rose Gold Ingot', count: 19, value: 1900, market: true },
      { itemId: 8, name: 'Extravagant Salvaged Earring', count: 4, value: 120000 },
    ],
  };
  assert.equal(isValidUpdate({ voyages: [incoming] }), true);
  assert.equal(isValidUpdate({ voyages: [{ ...incoming, world: 'x'.repeat(40) }] }), false);
  const voyage = mergeVoyage(null, incoming);
  assert.equal(needsPrice(voyage), true);

  const { voyage: priced, notice } = priceVoyage(voyage, { 5: 20000 });
  assert.equal(priced.items[0].marketValue, 380000);
  assert.equal(priced.items[1].marketValue, undefined);
  assert.equal(needsPrice(priced), false);

  // Reenviar a mesma viagem não apaga o preço.
  assert.equal(mergeVoyage(priced, incoming), null);

  const state = newState();
  state.notices.push(notice, sectorNotice(voyage));
  const alerts = pendingAlerts(state, at(1, 7));
  assert.equal(alertPayload(alerts[0], '').content, '💎 **Tatl** trouxe item de market: 19x Rose Gold Ingot ≈ 380.000 gil (Behemoth).');
  assert.equal(alertPayload(alerts[1], '').content, '🗺️ **Tatl** desbloqueou **Deep-sea Site 9**!');
  alerts.forEach((a) => markSent(state, a));
  assert.deepEqual(pendingAlerts(state, at(1, 7)), []);

  const loot = renderPanel(state, [priced], at(1, 12)).embeds[0].fields.find((f) => f.name.includes('Loot')).value;
  assert.equal(loot, '**≈ 500.000 gil** (inclui 🏷️ 380.000 de market)\n**Tatl**: 500.000 gil');
});

test('estoque baixo em rodadas quando as rotas são conhecidas', () => {
  const state = newState();
  const details = { rank: 130, exp: 0, build: 'SSUC', route: 'J > O > R > Z', tanks: 21, kits: 24, condition: 90 };
  applyUpdate(state, { subs: [0, 1, 2, 3].map((slot) => ({ slot, name: `Sub${slot}`, returnTime: at(2, 15), age: 0, details })) }, at(1, 9));
  applyUpdate(state, { supplies: { tanks: 160, kits: 65, age: 0 } }, at(1, 9));
  const [low] = pendingAlerts(state, at(1, 9));
  assert.equal(alertPayload(low, '').content, '⛽ Estoque baixo: restam **160 tanques de ceruleum** (≈ 7 viagens, 1 rodada).');
  applyUpdate(state, { supplies: { tanks: 168 + 30, kits: 65, age: 0 } }, at(1, 10));
  assert.deepEqual(pendingAlerts(state, at(1, 10)), []);
});

test('canteiros de fora não aparecem pelo nome nos avisos', () => {
  const state = newState();
  const outside = (key, name) => ({ key, name, nextWaterAt: at(2, 9), dieAt: at(3, 9), age: 0, indoor: false });
  applyUpdate(state, { plots: [outside('f1', 'Garden Patch 1'), outside('f2', 'Garden Patch 2'), plot('v1', 'Vaso 1', at(2, 9), at(3, 9))] }, at(1, 9));
  const [ready] = pendingAlerts(state, at(2, 9));
  assert.equal(alertPayload(ready, '').content, '🌱 A plantação já pode ser regada (**Vaso 1** e os canteiros de fora)!');
  delete state.plots.v1;
  const [dying] = pendingAlerts(state, at(3, 6)).filter((a) => a.kind === 'plotDying');
  assert.equal(alertPayload(dying, '').content, `⚠️ Se ninguém regar, a plantação morre <t:${at(3, 9)}:R>!`);
  assert.equal(alertPayload({ kind: 'plotReady', plots: Object.entries(state.plots).map(([key, p]) => ({ key, ...p })) }, '').content,
    '🌱 A plantação já pode ser regada!');
});

test('lembrete de sub e de vaso esquecidos, uma vez só', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: at(1, 10), age: 0 }, { slot: 1, name: 'Ciela', returnTime: at(1, 10), age: 0 }] }, at(1, 9));
  pendingAlerts(state, at(1, 10)).forEach((a) => markSent(state, a));
  // Ciela saiu de novo; Navi ficou parada.
  applyUpdate(state, { subs: [{ slot: 1, name: 'Ciela', returnTime: at(2, 10), age: 0 }] }, at(1, 11));
  assert.equal(nextEvent(state, at(1, 11)), at(1, 12));
  const [late] = pendingAlerts(state, at(1, 12));
  assert.equal(alertPayload(late, '').content, '⏰ **Navi** voltou há mais de 2 horas e ainda não saiu de novo.');
  markSent(state, late);
  assert.deepEqual(pendingAlerts(state, at(1, 13)), []);

  // Dois vasos da mesma casa: só o Vaso 1 foi regado depois que os dois podiam ser regados.
  const house = (n) => `339:5:12:0:${n}`;
  applyUpdate(state, { plots: [plot(house(1), 'Vaso 1', at(2, 9), at(3, 9)), plot(house(2), 'Vaso 2', at(2, 9), at(3, 9))] }, at(1, 9));
  pendingAlerts(state, at(2, 9)).forEach((a) => markSent(state, a));
  applyUpdate(state, { plots: [plot(house(1), 'Vaso 1', at(3, 10), at(4, 10))] }, at(2, 10));
  assert.deepEqual(pendingAlerts(state, at(2, 10)).filter((a) => a.kind === 'plotForgotten'), []);
  const [forgotten] = pendingAlerts(state, at(2, 10, 15)).filter((a) => a.kind === 'plotForgotten');
  assert.equal(alertPayload(forgotten, '').content, '🌱 **Vaso 2** ficou sem regar? Os outros vasos já foram regados.');
  markSent(state, forgotten);
  assert.deepEqual(pendingAlerts(state, at(2, 11)).filter((a) => a.kind === 'plotForgotten'), []);
});

test('buff da mesa pronto avisa uma vez, e buff visto já pronto não avisa', () => {
  const state = newState();
  applyUpdate(state, { buffs: { age: 0, slots: [{ slot: 0, itemId: 1, name: 'Grade 3 Wheel of Confrontation', hours: 2 }] } }, at(1, 9));
  assert.deepEqual(pendingAlerts(state, at(1, 10)), []);
  const [ready] = pendingAlerts(state, at(1, 11));
  assert.equal(alertPayload(ready, '').content, '⚙️ **Grade 3 Wheel of Confrontation** está pronto!');
  markSent(state, ready);
  applyUpdate(state, { buffs: { age: 0, slots: [{ slot: 0, itemId: 1, name: 'Grade 3 Wheel of Confrontation', hours: 2 }] } }, at(1, 12));
  assert.deepEqual(pendingAlerts(state, at(1, 12)), []);

  const fresh = newState();
  applyUpdate(fresh, { buffs: { age: 0, slots: [{ slot: 0, itemId: 1, name: 'Wheel', hours: 0 }] } }, at(1, 9));
  assert.deepEqual(pendingAlerts(fresh, at(1, 9)), []);
});

test('estoque: painel, aviso de estoque baixo uma vez e volta a avisar depois de repor', () => {
  const state = newState();
  applyUpdate(state, { supplies: { tanks: 579, kits: 65, age: 0 } }, at(1, 9));
  const field = renderPanel(state, [], at(1, 9)).embeds[0].fields.find((f) => f.name.includes('Estoque'));
  assert.equal(field.value, `**579** tanques de ceruleum\n**65** kits de reparo\nContado <t:${at(1, 9)}:R>`);
  assert.deepEqual(pendingAlerts(state, at(1, 9)), []);

  applyUpdate(state, { supplies: { tanks: 100, kits: 65, age: 0 } }, at(1, 10));
  const [low] = pendingAlerts(state, at(1, 10));
  assert.equal(alertPayload(low, '9').content, '<@&9> ⛽ Estoque baixo: restam **100 tanques de ceruleum**.');
  markSent(state, low);
  applyUpdate(state, { supplies: { tanks: 90, kits: 65, age: 0 } }, at(1, 11));
  assert.deepEqual(pendingAlerts(state, at(1, 11)), []);

  // Uma contagem mais antiga não vence a mais nova.
  applyUpdate(state, { supplies: { tanks: 579, kits: 65, age: 7200 } }, at(1, 12));
  assert.equal(state.supplies.tanks, 90);

  applyUpdate(state, { supplies: { tanks: 500, kits: 65, age: 0 } }, at(1, 12));
  applyUpdate(state, { supplies: { tanks: 110, kits: 10, age: 0 } }, at(1, 13));
  const [again] = pendingAlerts(state, at(1, 13));
  assert.equal(alertPayload(again, '').content, '⛽ Estoque baixo: restam **110 tanques de ceruleum** e **10 kits de reparo**.');
  assert.equal(isValidUpdate({ supplies: { tanks: -1, kits: 0, age: 0 } }), false);
});

test('plantas: lembrete 1 hora antes, aviso na hora e painel, por plot', () => {
  const state = newState();
  applyUpdate(state, { plots: [plot('a', 'Vaso 1', at(2, 9)), plot('b', 'Vaso 2', at(2, 9, 10)), plot('c', 'Canteiro', at(2, 15))] }, at(1, 9));
  assert.equal(renderPanel(state, [], at(1, 9)).embeds[0].fields[2].value,
    `**Vaso 1**: regar <t:${at(2, 9)}:R> (<t:${at(2, 9)}:t>)\n**Vaso 2**: regar <t:${at(2, 9, 10)}:R> (<t:${at(2, 9, 10)}:t>)\n**Canteiro**: regar <t:${at(2, 15)}:R> (<t:${at(2, 15)}:t>)`);
  const [reminder] = pendingAlerts(state, at(2, 8));
  assert.equal(alertPayload(reminder, '9').content, '<@&9> Lembrete: Em 1 hora a plantação estará disponível para regar (**Vaso 1** e **Vaso 2**).');
  markSent(state, reminder);
  const [ready] = pendingAlerts(state, at(2, 9, 10));
  assert.equal(alertPayload(ready, '9').content, '<@&9> 🌱 A plantação já pode ser regada (**Vaso 1** e **Vaso 2**)!');
  markSent(state, ready);
  assert.equal(pendingAlerts(state, at(2, 10)).length, 0);
  assert.equal(pendingAlerts(state, at(2, 14))[0].plots[0].name, 'Canteiro');
});

test('plantas perto de morrer: painel mostra quando morre e avisa 3, 2 e 1 hora antes', () => {
  const state = newState();
  applyUpdate(state, { plots: [plot('a', 'Vaso 1', at(2, 9), at(3, 9))] }, at(1, 9));
  assert.equal(renderPanel(state, [], at(2, 10)).embeds[0].fields[2].value, `**Vaso 1**: ✅ Pronta para regar. Morre <t:${at(3, 9)}:R>`);
  assert.equal(nextEvent(state, at(3, 5)), at(3, 6));
  const sent = [];
  for (let hour = 5; hour <= 9; hour += 1) {
    for (const alert of pendingAlerts(state, at(3, hour)).filter((a) => a.kind === 'plotDying')) {
      sent.push(hour);
      assert.equal(alertPayload(alert, '9').content, `<@&9> ⚠️ Se ninguém regar, morre: **Vaso 1** <t:${at(3, 9)}:R>!`);
      markSent(state, alert);
    }
  }
  assert.deepEqual(sent, [6, 7, 8]);
  applyUpdate(state, { plots: [plot('a', 'Vaso 1', at(4, 9), at(5, 9))] }, at(3, 8, 30));
  assert.equal(pendingAlerts(state, at(3, 8, 31)).length, 0);
});

test('rega antiga de outro PC não passa por cima da mais nova, e plot morto há dias some', () => {
  const state = newState();
  applyUpdate(state, { plots: [plot('a', 'Vaso 1', at(2, 9), at(3, 9))] }, at(1, 9));
  applyUpdate(state, { plots: [plot('a', 'Vaso 1', at(2, 5), at(3, 5), 4 * 3600)] }, at(1, 9, 1));
  assert.equal(state.plots.a.nextWaterAt, at(2, 9));
  assert.match(renderPanel(state, [], at(6, 0)).embeds[0].fields[2].value, /^Nenhum dado ainda/);
});

test('rega vista já vencida não gera aviso atrasado, e o formato antigo vira o plot Plantação', () => {
  const state = newState();
  applyUpdate(state, { garden: { nextWaterAt: at(1, 8), age: 0 } }, at(1, 9));
  assert.equal(state.plots.geral.name, 'Plantação');
  assert.equal(pendingAlerts(state, at(1, 9)).length, 0);
  applyUpdate(state, { garden: { nextWaterAt: at(2, 9), age: 0 } }, at(1, 9, 5));
  assert.equal(pendingAlerts(state, at(2, 8))[0].kind, 'plotReminder');
});

test('o plugin recebe o estado sem os dados internos e com os últimos 7 dias', () => {
  const state = newState();
  state.messageId = '1';
  const body = publicState(state, [], at(2, 10));
  assert.equal(body.messageId, undefined);
  assert.equal(body.alerts, undefined);
  assert.deepEqual(recentDays(at(7, 1)), ['2026-11-01', '2026-11-02', '2026-11-03', '2026-11-04', '2026-11-05', '2026-11-06', '2026-11-07']);
});

test('apagar um aviso também agenda o serviço', () => {
  const state = newState();
  state.alerts = [{ id: '1', deleteAt: at(1, 12) }];
  assert.equal(nextEvent(state, at(1, 10)), at(1, 12));
});

test('dados inválidos são recusados', () => {
  assert.equal(isValidUpdate({ subs: [{ slot: 0, name: 'Navi', returnTime: 'x', age: 0 }] }), false);
  assert.equal(isValidUpdate({ voyages: [{ slot: 0, subName: 'Navi', returnTime: 1, items: [] }] }), false);
  assert.equal(isValidUpdate({ subs: [{ slot: 0, name: 'Navi', returnTime: 1, age: 0 }] }), true);
  assert.equal(isValidUpdate({ garden: { nextWaterAt: -1, age: 0 } }), false);
  assert.equal(isValidUpdate({ plots: [{ key: '', name: 'x', nextWaterAt: 1, age: 0 }] }), false);
});
