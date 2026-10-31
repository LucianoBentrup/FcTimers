import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  alertDeleteAt, alertPayload, applyUpdate, dayOf, isValidUpdate, mergeVoyage, newState, nextEvent, nextMidnight, pendingReturns, renderPanel, voyageKey,
} from '../src/logic.js';

const at = (day, hour, minute = 0) => Math.floor(Date.UTC(2026, 10, day, hour + 3, minute) / 1000);
const voyage = (slot, returnTime, value, itemId = 1) => ({
  slot, subName: `Sub${slot}`, returnTime, items: [{ itemId, name: `Item${itemId}`, count: 1, value }],
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
    totals.push(panel.embeds[0].fields[2].value.split('\n')[0]);
  }
  assert.deepEqual(totals, ['**100 gil**', '**200 gil**', '**300 gil**', '**400 gil**']);
});

test('segunda rodada soma no valor do submarino', () => {
  const panel = renderPanel(newState(), [voyage(0, at(1, 8), 100), voyage(0, at(1, 20), 50)], at(1, 21));
  assert.match(panel.embeds[0].fields[2].value, /\*\*Sub0\*\*: 150 gil/);
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
  const { fields } = renderPanel(state, [], at(1, 15)).embeds[0];
  assert.equal(fields.at(-2).name, '📦 Itens de hoje');
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
  const panel = renderPanel(newState(), Array.from({ length: 8 }, (_, i) => ({ slot: i % 4, subName: 'x'.repeat(128), returnTime: at(1, 8), items })), at(1, 9));
  for (const field of panel.embeds[0].fields) assert.ok(field.value.length <= 1024, `${field.name}: ${field.value.length}`);
});

test('o próximo evento é o retorno mais próximo ou a meia-noite', () => {
  const state = newState();
  assert.equal(nextEvent(state, at(1, 10)), at(2, 0));
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: at(1, 12), age: 0 }] }, at(1, 10));
  assert.equal(nextEvent(state, at(1, 10)), at(1, 12));
});

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
  const one = alertPayload([{ name: 'Navi' }], '123');
  assert.equal(one.content, '<@&123> 🚢 **Navi** voltou!');
  assert.deepEqual(one.allowed_mentions, { parse: [], roles: ['123'] });
  assert.equal(alertPayload([{ name: 'Navi' }, { name: 'Tatl' }, { name: 'Tael' }], '123').content, '<@&123> 🚢 **Navi**, **Tatl** e **Tael** voltaram!');
});

test('o aviso dura até a volta do submarino mais demorado', () => {
  const state = newState();
  applyUpdate(state, { subs: [
    { slot: 0, name: 'Navi', returnTime: 2000, age: 0 },
    { slot: 1, name: 'Tatl', returnTime: 90000, age: 0 },
    { slot: 2, name: 'Ciela', returnTime: 40000, age: 0 },
  ] }, 1000);
  assert.equal(alertDeleteAt(state, 2000), 90000);
});

test('sem submarino em viagem, o aviso usa o limite de segurança', () => {
  const state = newState();
  applyUpdate(state, { subs: [{ slot: 0, name: 'Navi', returnTime: 2000, age: 0 }] }, 1000);
  assert.equal(alertDeleteAt(state, 2000), 2000 + 86400);
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
});
