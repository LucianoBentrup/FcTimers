import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  applyFields, attendees, changeDraft, closeDue, CLOSE_AFTER, draftReview, dueReminders, eventEmbed, eventFromDraft, eventModal, eventPost,
  eventsForPlugin, eventsWakeAt, KEEP_CLOSED, modalFields, newDraft, nextOccurrence, parseWhen, pruneDue, signUp, typePicker, zonedToUnix,
} from '../src/events.js';
import { parseCustomId } from '../src/menu.js';

// Quarta, 25/11/2026, 10:00 em Brasília (13:00 UTC).
const NOW = Math.floor(Date.UTC(2026, 10, 25, 13, 0) / 1000);
const brt = (month, day, hour, minute = 0) => Math.floor(Date.UTC(2026, month - 1, day, hour + 3, minute) / 1000);

const make = (type = 'group', size = 8, start = brt(11, 26, 21)) => {
  const draft = { ...newDraft(type, 'creator'), size, title: 'Savage', start };
  return eventFromDraft(draft, 'ev1', NOW);
};

test('data e hora no fuso da FC', () => {
  assert.equal(zonedToUnix(2026, 12, 2, 21, 0, 'America/Sao_Paulo'), brt(12, 2, 21));
  assert.equal(parseWhen('2026-12-02 21:00', NOW).start, brt(12, 2, 21));
  assert.equal(parseWhen('02/12 21h', NOW).start, brt(12, 2, 21));
  assert.equal(parseWhen('amanhã 20:30', NOW).start, brt(11, 26, 20, 30));
  assert.equal(parseWhen('tomorrow 8pm', NOW).start, brt(11, 26, 20));
  assert.equal(parseWhen('hoje 21:00', NOW).start, brt(11, 25, 21));
  // Hoje é quarta. "ter" é a próxima terça; "qua" às 9h já passou, então é a quarta que vem.
  assert.equal(parseWhen('ter 21:00', NOW).start, brt(12, 1, 21));
  assert.equal(parseWhen('Tuesday 21:00', NOW).start, brt(12, 1, 21));
  assert.equal(parseWhen('qua 9:00', NOW).start, brt(12, 2, 9));
  assert.equal(parseWhen('qua 11:00', NOW).start, brt(11, 25, 11));
  // Só a hora: hoje, ou amanhã se já passou.
  assert.equal(parseWhen('9:30', NOW).start, brt(11, 26, 9, 30));
  // Data sem ano que já passou vai para o ano que vem.
  assert.equal(parseWhen('01/01 20:00', NOW).start, Math.floor(Date.UTC(2027, 0, 1, 23) / 1000));
});

test('data que não dá para ler ou que já passou', () => {
  for (const value of ['', 'sexta', '21', 'amanha', '32/12 20:00', '2026-12-02 25:00', 'quarta-feira 20:00', 'next week 20:00']) {
    assert.equal(parseWhen(value, NOW).error, 'badWhen', value);
  }
  assert.equal(parseWhen('2026-11-20 20:00', NOW).error, 'pastWhen');
  assert.equal(parseWhen('hoje 8:00', NOW).error, 'pastWhen');
});

test('o formulário vira rascunho e diz o que falta', () => {
  const draft = applyFields(newDraft('other', 'u'), { title: '  Mapas  G17 ', when: 'amanhã 20:00', field: 'G17', desc: 'Traga mapa' }, NOW);
  assert.equal(draft.title, 'Mapas G17');
  assert.equal(draft.start, brt(11, 26, 20));
  assert.equal(draft.error, null);
  assert.equal(applyFields(newDraft('other', 'u'), { title: '', when: 'amanhã 20:00' }, NOW).error, 'noTitle');
  assert.equal(applyFields(newDraft('other', 'u'), { title: 'X', when: 'logo' }, NOW).error, 'badWhen');
  assert.equal(applyFields(newDraft('other', 'u'), { title: 'x'.repeat(200), when: '9:00' }, NOW).title.length, 60);
});

test('os botões da revisão mudam o rascunho', () => {
  const draft = newDraft('group', 'u');
  assert.ok(changeDraft(draft, 'size', '24'));
  assert.equal(draft.size, 24);
  changeDraft(draft, 'size', '5');
  assert.equal(draft.size, 24);
  changeDraft(draft, 'repeat');
  assert.equal(draft.repeat, true);
  assert.deepEqual(['dm', 'both', 'channel'].map(() => { changeDraft(draft, 'notify'); return draft.notify; }), ['dm', 'both', 'channel']);
  assert.deepEqual(['15m', 'both', '1h'].map(() => { changeDraft(draft, 'lead'); return draft.lead; }), ['15m', 'both', '1h']);
  assert.equal(changeDraft(draft, 'apagar'), false);
  // Tamanho só existe em conteúdo em grupo.
  const other = newDraft('other', 'u');
  changeDraft(other, 'size', '4');
  assert.equal(other.size, 8);
});

test('inscrição: entra, troca de função e sai clicando de novo', () => {
  const event = make();
  assert.equal(signUp(event, 'a', 'tank').outcome, 'joined');
  assert.equal(signUp(event, 'a', 'dps').outcome, 'joined');
  assert.deepEqual(event.roles.tank, []);
  assert.deepEqual(event.roles.dps, ['a']);
  assert.equal(signUp(event, 'a', 'dps').outcome, 'left');
  assert.deepEqual(attendees(event), []);
  assert.equal(signUp(event, 'a', 'cant').outcome, 'cant');
  assert.equal(signUp(event, 'a', 'cant').outcome, 'left');
  assert.deepEqual(event.cant, []);
});

test('função cheia manda para a reserva, e quem sai abre vaga para o primeiro da fila', () => {
  const event = make('group', 4);
  signUp(event, 'a', 'tank');
  const full = signUp(event, 'b', 'tank');
  assert.equal(full.outcome, 'reserve');
  assert.equal(full.position, 1);
  assert.equal(signUp(event, 'c', 'backup').position, 2);
  // Sai o tank: sobe o b, que pediu tank, e não o c, que entrou depois.
  const left = signUp(event, 'a', 'tank');
  assert.equal(left.outcome, 'left');
  assert.deepEqual(left.promoted, [{ user: 'b', role: 'tank' }]);
  assert.deepEqual(event.roles.tank, ['b']);
  assert.deepEqual(event.reserve, [{ user: 'c', role: 'any' }]);
});

test('"Reserva" cobre qualquer função que abrir', () => {
  const event = make('group', 4);
  signUp(event, 'h', 'healer');
  signUp(event, 'r', 'backup');
  signUp(event, 'x', 'healer');
  const result = signUp(event, 'h', 'cant');
  assert.deepEqual(result.promoted, [{ user: 'r', role: 'healer' }]);
  assert.deepEqual(event.reserve, [{ user: 'x', role: 'healer' }]);
  // Sair da reserva clicando de novo.
  assert.equal(signUp(event, 'x', 'healer').outcome, 'left');
  assert.deepEqual(event.reserve, []);
});

test('Encontro e Outro: Vou e Talvez, sem limite e sem reserva', () => {
  const event = make('gathering');
  for (const user of ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j']) assert.equal(signUp(event, user, 'going').outcome, 'joined');
  signUp(event, 'k', 'maybe');
  assert.equal(attendees(event).length, 11);
  assert.equal(signUp(event, 'k', 'tank').outcome, 'closed');
  assert.equal(signUp(event, 'k', 'backup').outcome, 'closed');
});

test('evento fechado não aceita inscrição', () => {
  const event = make();
  event.status = 'cancelled';
  assert.equal(signUp(event, 'a', 'tank').outcome, 'closed');
});

test('lembretes saem uma vez, no máximo 2, e não depois do início', () => {
  const event = make('other', 8, NOW + 7200);
  event.lead = 'both';
  assert.deepEqual(dueReminders(event, NOW), []);
  assert.deepEqual(dueReminders(event, NOW + 3600), [3600]);
  event.sent = [3600];
  assert.deepEqual(dueReminders(event, NOW + 3600), []);
  assert.deepEqual(dueReminders(event, NOW + 7200 - 900), [900]);
  assert.deepEqual(dueReminders({ ...event, sent: [] }, NOW + 7200), []);
});

test('o evento fecha 2 horas depois do início e sai da lista depois de 14 dias', () => {
  const event = make('other', 8, NOW + 60);
  assert.equal(closeDue(event, NOW + 60 + CLOSE_AFTER - 1), false);
  assert.equal(closeDue(event, NOW + 60 + CLOSE_AFTER), true);
  Object.assign(event, { status: 'done', closedAt: NOW });
  assert.equal(pruneDue(event, NOW + KEEP_CLOSED - 1), false);
  assert.equal(pruneDue(event, NOW + KEEP_CLOSED), true);
});

test('repetição semanal: mesma hora na semana seguinte, lista limpa', () => {
  const event = make();
  signUp(event, 'a', 'tank');
  event.sent = [3600];
  const next = nextOccurrence(event, 'ev2', event.start + CLOSE_AFTER);
  assert.equal(next.start, event.start + (7 * 86400));
  assert.equal(next.series, 'ev1');
  assert.deepEqual(attendees(next), []);
  assert.deepEqual(next.sent, []);
  assert.equal(next.messageId, null);
  // Serviço parado por 3 semanas: pula para a próxima no futuro.
  assert.ok(nextOccurrence(event, 'ev3', event.start + (20 * 86400)).start > event.start + (20 * 86400));
});

test('próximo momento dos eventos', () => {
  const event = make('other', 8, NOW + 7200);
  assert.equal(eventsWakeAt({ ev1: event }, NOW), NOW + 3600);
  event.sent = [3600];
  assert.equal(eventsWakeAt({ ev1: event }, NOW), NOW + 7200 + CLOSE_AFTER);
  assert.equal(eventsWakeAt({}, NOW), Infinity);
});

test('o post tem no máximo 5 botões por linha, todos entendidos pelo parser', () => {
  for (const type of ['group', 'gathering', 'other']) {
    const post = eventPost(make(type), 'pt');
    for (const r of post.components) {
      assert.ok(r.components.length <= 5);
      for (const b of r.components) assert.ok(parseCustomId(b.custom_id), b.custom_id);
    }
    assert.deepEqual(post.allowed_mentions, { parse: [] });
  }
  const labels = eventPost(make('group'), 'en').components[0].components.map((b) => b.label);
  assert.deepEqual(labels, ['Tank', 'Healer', 'DPS', 'Reserve', "Can't go"]);
  assert.deepEqual(eventPost(make('gathering'), 'pt').components[0].components.map((b) => b.label), ['Vou', 'Talvez', 'Não vou']);
});

test('o post mostra vagas, reserva e quem não vai; fechado fica sem botões', () => {
  const event = make('group', 4);
  signUp(event, 'a', 'tank');
  signUp(event, 'b', 'tank');
  signUp(event, 'c', 'cant');
  const embed = eventEmbed(event, 'en');
  assert.match(embed.fields[0].name, /Tank 1\/1/);
  assert.match(embed.fields[2].value, /open/);
  assert.match(embed.fields.find((f) => f.name.includes('Reserve')).value, /1\. <@b> \(Tank\)/);
  assert.match(embed.fields.find((f) => f.name.includes("Can't go")).value, /<@c>/);
  assert.match(embed.footer.text, /1 of 4 spots filled/);
  event.status = 'cancelled';
  const closed = eventPost(event, 'en');
  assert.deepEqual(closed.components, []);
  assert.match(closed.embeds[0].title, /cancelled/);
});

test('o formulário respeita os limites do Discord e volta preenchido ao editar', () => {
  const draft = { ...newDraft('gathering', 'u'), title: 'Festa', when: 'sab 19:00' };
  const modal = eventModal(draft, 'pt');
  assert.ok(modal.title.length <= 45);
  assert.equal(modal.components.length, 4);
  assert.equal(modal.custom_id, 'fc:evm:gathering');
  assert.equal(modal.components[0].components[0].value, 'Festa');
  assert.equal(modal.components[2].components[0].label, 'Local');
  assert.deepEqual(parseCustomId(modal.custom_id), { kind: 'evmodal', type: 'gathering' });
});

test('campos do formulário enviado, com ou sem embrulho', () => {
  const plain = { components: [{ type: 1, components: [{ custom_id: 'title', value: 'A' }] }, { type: 1, components: [{ custom_id: 'when', value: 'B' }] }] };
  assert.deepEqual(modalFields(plain), { title: 'A', when: 'B' });
  const labelled = { components: [{ type: 18, component: { custom_id: 'title', value: 'C' } }] };
  assert.deepEqual(modalFields(labelled), { title: 'C' });
});

test('revisão: com erro, Postar fica desligado e o motivo aparece', () => {
  const bad = applyFields(newDraft('group', 'u'), { title: 'X', when: 'logo' }, NOW);
  const screen = draftReview(bad, 'pt', '5');
  assert.match(screen.content, /Não entendi a data/);
  assert.equal(screen.embeds.length, 0);
  const post = screen.components.flatMap((r) => r.components).find((b) => b.custom_id === 'fc:evd:post');
  assert.equal(post.disabled, true);
  const good = applyFields(newDraft('group', 'u'), { title: 'X', when: 'amanhã 21:00' }, NOW);
  const ok = draftReview(good, 'pt', '5');
  assert.match(ok.content, /<#5>/);
  assert.equal(ok.embeds.length, 1);
  assert.ok(ok.components[0].components.some((b) => b.custom_id === 'fc:evd:size:24'));
  for (const id of ok.components.flatMap((r) => r.components).map((b) => b.custom_id)) assert.ok(parseCustomId(id), id);
});

test('a escolha do tipo leva ao formulário certo', () => {
  const ids = typePicker('en').components[0].components.map((b) => b.custom_id);
  assert.deepEqual(ids, ['fc:evnew:group', 'fc:evnew:gathering', 'fc:evnew:other', 'fc:events']);
  for (const id of ids) assert.ok(parseCustomId(id));
});

test('o plugin recebe os eventos abertos sem quem se inscreveu', () => {
  const event = make();
  signUp(event, 'a', 'tank');
  const [sent] = eventsForPlugin({ ev1: event, ev2: { ...make(), id: 'ev2', status: 'done' } });
  assert.equal(sent.signedUp, 1);
  assert.equal(sent.capacity, 8);
  assert.ok(!JSON.stringify(sent).includes('"a"'));
});
