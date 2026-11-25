// Roda o serviço localmente com um Discord simulado. Nada sai para a internet nem para a sua conta.
import assert from 'node:assert/strict';
import { spawn, execSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const SERVICE_PORT = 8788;
const DISCORD_PORT = 8799;
const KEY = 'chave-local-de-teste';
const service = `http://127.0.0.1:${SERVICE_PORT}`;
const sleep = (ms) => new Promise((resolve) => { setTimeout(resolve, ms); });

const calls = [];
let failNext = 0;
let nextId = 100;
const messages = new Map();
// Id da resposta de cada interação (o menu postado no canal), pelo token dela.
const originals = new Map();
const discord = createServer((req, res) => {
  let data = '';
  req.on('data', (chunk) => { data += chunk; });
  req.on('end', () => {
    if (failNext > 0) {
      failNext -= 1;
      calls.push({ method: req.method, failed: true });
      res.writeHead(500).end();
      return;
    }
    if (req.url.includes('/messages/@original')) {
      const token = req.url.split('/messages/@original')[0].split('/').at(-1);
      if (!originals.has(token)) originals.set(token, String(nextId++));
      calls.push({ method: req.method, original: true, url: req.url, body: data ? JSON.parse(data) : null });
      res.writeHead(req.method === 'DELETE' ? 204 : 200, { 'Content-Type': 'application/json' }).end(req.method === 'DELETE' ? undefined : JSON.stringify({ id: originals.get(token) }));
      return;
    }
    if (req.method === 'GET' && req.url.startsWith('/api/v10/guilds/')) {
      calls.push({ method: 'GET', url: req.url });
      res.writeHead(200, { 'Content-Type': 'application/json' }).end(JSON.stringify({ id: req.url.split('/').at(-1), name: 'Servidor Teste' }));
      return;
    }
    const id = req.url.match(/messages\/(\d+)/)?.[1];
    if (req.method === 'DELETE') {
      messages.delete(id);
      calls.push({ method: 'DELETE', messageId: id, url: req.url });
      res.writeHead(204).end();
      return;
    }
    if (req.method === 'PATCH' && !messages.has(id)) {
      calls.push({ method: 'PATCH', missing: true });
      res.writeHead(404).end();
      return;
    }
    const messageId = id ?? String(nextId++);
    const body = JSON.parse(data);
    messages.set(messageId, body);
    calls.push({ method: req.method, messageId, url: req.url, auth: req.headers.authorization, body });
    res.writeHead(200, { 'Content-Type': 'application/json' }).end(JSON.stringify({ id: messageId }));
  });
});

const post = (body, key = KEY) => fetch(`${service}/update`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});

function field(name) {
  const last = [...messages.values()].filter((m) => m.embeds).at(-1);
  return last.embeds[0].fields.find((f) => f.name.includes(name)).value;
}

async function waitFor(check, label) {
  for (let i = 0; i < 120; i += 1) {
    if (check()) return;
    await sleep(500);
  }
  throw new Error(`Tempo esgotado: ${label}`);
}

let passed = 0;
function ok(name) {
  passed += 1;
  console.log(`OK    ${name}`);
}

const persist = mkdtempSync(join(tmpdir(), 'fc-timers-'));
// Chave de teste do app do Discord, gerada aqui, para assinar os comandos como o Discord faz.
const discordKeys = await crypto.subtle.generateKey({ name: 'Ed25519' }, true, ['sign', 'verify']);
const discordPublic = Buffer.from(await crypto.subtle.exportKey('raw', discordKeys.publicKey)).toString('hex');
writeFileSync('.dev.vars', `DISCORD_WEBHOOK=http://127.0.0.1:${DISCORD_PORT}/api/webhooks/1/abc\nFC_KEY=${KEY}\nROLE_ID=999\n`
  + `DISCORD_PUBLIC_KEY=${discordPublic}\nDISCORD_GUILD_ID=42\nDISCORD_APP_ID=7\n`
  + `DISCORD_API=http://127.0.0.1:${DISCORD_PORT}/api/v10\nDISCORD_BOT_TOKEN=bot-de-teste\n`);

async function interaction(payload, sign = true) {
  const body = JSON.stringify(payload);
  const timestamp = String(Math.floor(Date.now() / 1000));
  const signature = sign
    ? Buffer.from(await crypto.subtle.sign('Ed25519', discordKeys.privateKey, new TextEncoder().encode(timestamp + body))).toString('hex')
    : '00'.repeat(64);
  return fetch(`${service}/interactions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Signature-Ed25519': signature, 'X-Signature-Timestamp': timestamp },
    body,
  });
}
await new Promise((resolve) => { discord.listen(DISCORD_PORT, '127.0.0.1', resolve); });

const worker = spawn(`npx wrangler dev --port ${SERVICE_PORT} --persist-to "${persist}" --log-level error`, { shell: true, stdio: 'ignore', detached: process.platform !== 'win32' });

let exitCode = 1;
try {
  await waitFor(() => false || true, 'início');
  for (let i = 0; i < 120; i += 1) {
    try {
      await fetch(`${service}/history`);
      break;
    } catch {
      await sleep(1000);
    }
  }

  assert.equal((await post({ subs: [] }, 'chave-errada')).status, 401);
  ok('chave errada é recusada');
  assert.equal(calls.length, 0);

  assert.equal((await post({ subs: [{ slot: 0, name: 'Navi', returnTime: 'x', age: 0 }] })).status, 400);
  ok('dados inválidos são recusados');

  const now = Math.floor(Date.now() / 1000);
  const earlier = now - 60;
  const voyage = (slot, returnTime, value) => ({
    slot, subName: ['Navi', 'Tatl', 'Ciela', 'Tael'][slot], returnTime, items: [{ itemId: 1, name: 'Item A', count: 2, value }],
  });

  // 2 pessoas enviando ao mesmo tempo, com a mesma viagem.
  const results = await Promise.all([
    post({ subs: [{ slot: 0, name: 'Navi', returnTime: now + 14400, age: 0 }], voyages: [voyage(0, earlier, 100)] }),
    post({ subs: [{ slot: 1, name: 'Tatl', returnTime: now + 18000, age: 0 }], voyages: [voyage(0, earlier, 100)] }),
    post({ buffs: { age: 0, slots: [{ slot: 5, itemId: 9669, name: 'Grade 3 Wheel', hours: 70 }] } }),
  ]);
  assert.ok(results.every((r) => r.ok));

  await waitFor(() => calls.some((c) => c.method === 'POST'), 'primeira mensagem');
  await sleep(1000);
  assert.equal(messages.size, 1);
  assert.equal(calls.filter((c) => c.method === 'POST').length, 1);
  ok('envios simultâneos geram uma única mensagem');
  assert.match(field('Submarinos'), /Navi/);
  assert.match(field('Submarinos'), /Tatl/);
  assert.match(field('Buffs'), /Grade 3 Wheel/);
  ok('envios simultâneos não apagam os dados um do outro');
  assert.match(field('Loot'), /^\*\*100 gil\*\*/);
  ok('mesma viagem de 2 pessoas conta uma vez');

  let before = calls.length;
  await post({ activeBuffs: { age: 0, buffs: [{ name: 'Seal Sweetener II', seconds: 86392 }] } });
  await waitFor(() => calls.length > before, 'buffs ativos');
  await sleep(500);
  assert.match(field('Buffs ativos'), /Seal Sweetener II\*\*: <t:\d+:R>/);
  assert.match(field('Loot'), /^\*\*100 gil\*\*/);
  ok('buffs ativos aparecem sem mexer no loot');

  // Próximas coletas acumulam na mesma mensagem.
  let edits = calls.length;
  await post({ voyages: [voyage(1, earlier + 1, 200)] });
  await waitFor(() => calls.length > edits, 'edição');
  await sleep(500);
  assert.match(field('Loot'), /^\*\*300 gil\*\*/);
  assert.equal(messages.size, 1);
  ok('nova coleta soma e edita a mesma mensagem');

  // Reenvio repetido: nada muda.
  edits = calls.length;
  await post({ voyages: [voyage(1, earlier + 1, 200)] });
  await sleep(3500);
  assert.match(field('Loot'), /^\*\*300 gil\*\*/);
  ok('reenvio da mesma viagem não soma');

  // Falha do Discord: o estado fica salvo e o painel se corrige.
  failNext = 1;
  edits = calls.length;
  await post({ voyages: [voyage(2, earlier + 2, 300)] });
  await waitFor(() => calls.slice(edits).some((c) => c.failed), 'falha simulada');
  const history = await (await fetch(`${service}/history`, { headers: { Authorization: `Bearer ${KEY}` } })).json();
  assert.equal(history.voyages.length, 3);
  ok('falha do Discord não perde a viagem');

  // Uma atualização nova força o painel a se corrigir antes da nova tentativa.
  await post({ voyages: [voyage(3, earlier + 3, 400)] });
  await waitFor(() => /1\.000 gil/.test(field('Loot')), 'painel corrigido');
  ok('o painel se corrige depois da falha');

  // Mensagem apagada: cria outra.
  messages.clear();
  await post({ voyages: [voyage(3, earlier + 4, 1)] });
  await waitFor(() => messages.size === 1, 'nova mensagem');
  assert.match(field('Loot'), /1\.001 gil/);
  ok('mensagem apagada é recriada com o total certo');

  // Submarino volta com todos offline: o serviço avisa sozinho, uma vez, e o painel continua sendo um só.
  const alerts = () => [...messages.values()].filter((m) => m.content);
  const backAt = Math.floor(Date.now() / 1000) + 8;
  await post({ subs: [{ slot: 2, name: 'Ciela', returnTime: backAt, age: 0 }] });
  await sleep(4000);
  assert.equal(alerts().length, 0);
  await waitFor(() => alerts().length === 1, 'aviso de retorno');
  assert.equal(alerts()[0].content, '<@&999> 🚢 **Ciela** voltou!');
  assert.deepEqual(alerts()[0].allowed_mentions.roles, ['999']);
  await waitFor(() => /Ciela\*\*: ✅ pronto/.test(field('Submarinos')), 'painel com pronto');
  ok('submarino que volta gera aviso marcando o cargo, e o painel muda');
  await post({ subs: [{ slot: 2, name: 'Ciela', returnTime: backAt, age: 0 }] });
  await sleep(3500);
  assert.equal(alerts().length, 1);
  assert.equal([...messages.values()].filter((m) => m.embeds).length, 1);
  ok('reler o mesmo submarino não repete o aviso');

  // O plugin recebe o estado de volta, sem os dados internos do Discord.
  await post({ plots: [{ key: 'casa:1', name: 'Vaso 1', nextWaterAt: Math.floor(Date.now() / 1000) + 86400, dieAt: Math.floor(Date.now() / 1000) + 172800, age: 0 }] });
  const state = await (await fetch(`${service}/state`, { headers: { Authorization: `Bearer ${KEY}` } })).json();
  assert.equal(state.subs[2].name, 'Ciela');
  assert.ok(state.plots['casa:1'].nextWaterAt > 0);
  assert.equal(state.voyages.length, 5);
  assert.equal(state.messageId, undefined);
  assert.equal((await fetch(`${service}/state`)).status, 401);
  ok('o plugin recebe o estado, as plantas e as viagens');
  await waitFor(() => /Vaso 1\*\*: regar <t:\d+:R>/.test(field('Plantação')), 'plantas no painel');
  ok('as plantas aparecem no painel');

  // Reenviar o painel apaga a mensagem antiga e cria outra no fim do canal.
  const oldPanel = [...messages.entries()].filter(([, m]) => m.embeds).at(-1)[0];
  assert.equal((await fetch(`${service}/repost`, { method: 'POST' })).status, 401);
  const repost = await fetch(`${service}/repost`, { method: 'POST', headers: { Authorization: `Bearer ${KEY}` } });
  assert.equal(repost.status, 200);
  await waitFor(() => !messages.has(oldPanel) && [...messages.values()].some((m) => m.embeds), 'painel reenviado');
  assert.equal([...messages.values()].filter((m) => m.embeds).length, 1);
  ok('o painel é reenviado no fim do canal');

  // Cada personagem guarda só o último log enviado.
  const auth = { Authorization: `Bearer ${KEY}`, 'Content-Type': 'application/json' };
  const sendLog = (text) => fetch(`${service}/log`, { method: 'POST', headers: auth, body: JSON.stringify({ character: 'Luciano', text }) });
  assert.equal((await sendLog('primeiro')).status, 200);
  assert.equal((await sendLog('segundo')).status, 200);
  assert.equal((await fetch(`${service}/log`, { method: 'POST', headers: auth, body: '{}' })).status, 400);
  assert.equal((await fetch(`${service}/logs`)).status, 401);
  const { logs } = await (await fetch(`${service}/logs`, { headers: auth })).json();
  assert.equal(logs.length, 1);
  assert.equal(logs[0].text, 'segundo');
  ok('o log de diagnóstico fica guardado no serviço');

  // Comandos do Discord: assinatura, PING, o /fcc e os botões do menu.
  assert.equal((await interaction({ type: 1 }, false)).status, 401);
  assert.deepEqual(await (await interaction({ type: 1 })).json(), { type: 1 });
  const member = (permissions) => ({ permissions, user: { id: '555' } });
  const command = (name, permissions = '0', options) => ({
    type: 2, guild_id: '42', token: 't', member: member(permissions), data: { name, ...(options ? { options } : {}) },
  });
  const click = (customId, permissions = '0') => ({
    type: 3, guild_id: '42', token: 't', member: member(permissions), data: { custom_id: customId, component_type: 2 },
  });
  const customIds = (data) => data.components.flatMap((r) => r.components).map((b) => b.custom_id);

  // O /fcc abre no canal, igual pra todo mundo: Ajustes aparece sempre e a permissão é conferida no clique.
  const menu = await (await interaction(command('fcc'))).json();
  assert.equal(menu.type, 4);
  assert.equal(menu.data.flags, undefined);
  assert.match(menu.data.embeds[0].fields[0].value, /Ciela/);
  assert.equal(menu.data.embeds[0].footer, undefined);
  assert.ok(customIds(menu.data).includes('fc:subs'));
  assert.ok(customIds(menu.data).includes('fc:manage'));
  assert.ok(!customIds(menu.data).some((id) => id.startsWith('fc:share')));
  const managerMenu = await (await interaction(command('fcc', '32'))).json();
  assert.deepEqual(customIds(managerMenu.data), customIds(menu.data));

  // Cliques no menu do canal: as abas mudam a mesma mensagem pra todos; o que é de uma pessoa abre só pra ela.
  const onChannel = (data) => ({ ...data, message: { id: '1', flags: 0 } });
  const publicLoot = await (await interaction(onChannel(click('fc:loot:week')))).json();
  assert.equal(publicLoot.type, 7);
  assert.equal(publicLoot.data.flags, undefined);
  assert.match(publicLoot.data.embeds[0].description, /7 dias/);
  const publicManage = await (await interaction(onChannel(click('fc:manage', '32')))).json();
  assert.equal(publicManage.type, 4);
  assert.equal(publicManage.data.flags, 64);
  assert.ok(customIds(publicManage.data).includes('fc:do:repost'));
  assert.match((await (await interaction(onChannel(click('fc:manage')))).json()).data.content, /Só quem gerencia/);
  const publicDm = await (await interaction(onChannel(click('fc:pref:dm')))).json();
  assert.equal(publicDm.type, 4);
  assert.equal(publicDm.data.flags, 64);
  assert.match(publicDm.data.content, /lembrete por DM/);
  await interaction(onChannel(click('fc:pref:dm')));
  const publicEvents = await (await interaction(onChannel(click('fc:events')))).json();
  assert.equal(publicEvents.type, 7);
  assert.ok(customIds(publicEvents.data).includes('fc:evnew'));

  // Um /fcc novo no mesmo canal apaga o anterior, pelo bot.
  const inChannelCmd = (token) => ({ ...command('fcc'), token, channel_id: '700' });
  await interaction(inChannelCmd('menu1'));
  await waitFor(() => calls.some((c) => c.original && c.method === 'GET' && c.url.includes('/menu1/')), 'id do primeiro menu');
  await interaction(inChannelCmd('menu2'));
  await waitFor(() => calls.some((c) => c.method === 'DELETE' && c.url === `/api/v10/channels/700/messages/${originals.get('menu1')}`), 'menu anterior apagado');
  await sleep(300);
  assert.ok(!calls.some((c) => c.method === 'DELETE' && c.url === `/api/v10/channels/700/messages/${originals.get('menu2')}`), 'o menu novo fica');

  const subsView = await (await interaction(click('fc:subs'))).json();
  assert.equal(subsView.type, 7);
  assert.match(subsView.data.embeds[0].description, /Ciela/);
  const lootView = await (await interaction(click('fc:loot:week'))).json();
  assert.match(lootView.data.embeds[0].description, /7 dias/);
  const shared = await (await interaction(click('fc:share:subs'))).json();
  assert.equal(shared.type, 4);
  assert.equal(shared.data.flags, undefined);
  assert.match(shared.data.content, /<@555>/);

  const blocked = await (await interaction(click('fc:do:repost'))).json();
  assert.match(blocked.data.content, /Só quem gerencia o servidor/);
  assert.match((await (await interaction(click('fc:manage'))).json()).data.content, /server managers/);
  const otherGuild = await (await interaction({ ...command('fcc'), guild_id: '1' })).json();
  assert.match(otherGuild.data.content, /não está ligado/);
  const unknown = await (await interaction(click('fc:nada'))).json();
  assert.equal(unknown.data.flags, 64);

  const reposted = await (await interaction(click('fc:do:repost', '32'))).json();
  assert.equal(reposted.type, 7);
  assert.match(reposted.data.content, /Painel reenviado/);
  const weeklyOff = await (await interaction(click('fc:do:weekly-off', '32'))).json();
  assert.match(weeklyOff.data.content, /desligado/);
  assert.ok(customIds(weeklyOff.data).includes('fc:do:weekly-on'));
  const english = await (await interaction(click('fc:do:lang-en', '32'))).json();
  assert.match(english.data.content, /English/);
  await interaction(click('fc:do:lang-pt', '32'));
  const logReply = await interaction(click('fc:do:log', '32'));
  assert.match(logReply.headers.get('content-type'), /multipart\/form-data/);
  assert.match(await logReply.text(), /segundo/);

  // Agrupar avisos demora: responde já e edita o menu depois, com o resultado.
  const deferred = await (await interaction(click('fc:do:regroup', '32'))).json();
  assert.deepEqual(deferred, { type: 6 });
  await waitFor(() => calls.some((c) => c.original && /agrupados/.test(c.body?.content ?? '')), 'menu editado depois de agrupar');
  const edited = calls.find((c) => c.original && c.method === 'PATCH').body;
  assert.equal(edited.flags, undefined);
  assert.ok(edited.components.length > 0);

  // Comando antigo ainda na lista do Discord: abre a seção certa do /fcc.
  const legacy = await (await interaction(command('estoque'))).json();
  assert.equal(legacy.data.flags, undefined);
  assert.match(legacy.data.content, /\/fcc/);
  assert.ok(legacy.data.components);
  ok('o /fcc abre o menu e os botões respondem com assinatura e permissão');

  // O bot: o painel muda do webhook para um canal, com botões embaixo.
  const inChannel = (data, channel) => ({ ...data, channel_id: channel });
  const movedAt = calls.length;
  const moved = await (await interaction(inChannel(click('fc:do:panel-here', '32'), '500'))).json();
  assert.match(moved.data.content, /<#500>/);
  await waitFor(() => calls.slice(movedAt).some((c) => c.method === 'POST' && c.url === '/api/v10/channels/500/messages'), 'painel postado pelo bot');
  const botPanel = calls.slice(movedAt).find((c) => c.url === '/api/v10/channels/500/messages');
  assert.equal(botPanel.auth, 'Bot bot-de-teste');
  assert.deepEqual(botPanel.body.components[0].components.map((b) => b.custom_id), ['fc:open:loot:day', 'fc:open:supplies', 'fc:open:events']);
  assert.ok(calls.slice(movedAt).some((c) => c.method === 'DELETE' && c.url.startsWith('/api/webhooks/')), 'o painel do webhook é apagado');
  const fromPanel = await (await interaction(click('fc:open:supplies'))).json();
  assert.equal(fromPanel.type, 4);
  assert.equal(fromPanel.data.flags, undefined);
  ok('o painel sai pelo bot, com botões que abrem o /fcc');

  // Eventos: canal, tipo, formulário, revisão e post.
  const noChannel = await (await interaction(click('fc:evnew', '32'))).json();
  assert.match(noChannel.data.content, /Eventos aqui/);
  await interaction(inChannel(click('fc:do:events-here', '32'), '600'));
  const blockedEvent = await (await interaction(click('fc:evnew'))).json();
  assert.match(blockedEvent.data.content, /Só quem gerencia/);
  assert.equal(blockedEvent.data.flags, 64);
  const picker = await (await interaction(click('fc:evnew', '32'))).json();
  assert.ok(customIds(picker.data).includes('fc:evnew:group'));
  const modal = await (await interaction(click('fc:evnew:group', '32'))).json();
  assert.equal(modal.type, 9);
  assert.equal(modal.data.custom_id, 'fc:evm:group');
  const submit = (fields) => ({
    type: 5, guild_id: '42', token: 't', member: member('32'),
    data: { custom_id: 'fc:evm:group', components: Object.entries(fields).map(([id, value]) => ({ type: 1, components: [{ type: 4, custom_id: id, value }] })) },
  });
  const badDate = await (await interaction(submit({ title: 'Savage', when: 'logo' }))).json();
  assert.match(badDate.data.content, /Não entendi a data/);
  const review = await (await interaction(submit({ title: 'Savage de terça', when: 'amanhã 21:00', field: 'M1S', desc: '' }))).json();
  assert.equal(review.type, 7);
  assert.match(review.data.embeds[0].title, /Savage de terça/);
  await interaction(click('fc:evd:size:4', '32'));
  await interaction(click('fc:evd:repeat', '32'));
  const posting = calls.length;
  const posted = await (await interaction(click('fc:evd:post', '32'))).json();
  assert.match(posted.data.content, /postado em <#600>/);
  const eventCall = calls.slice(posting).find((c) => c.method === 'POST' && c.url === '/api/v10/channels/600/messages');
  assert.ok(eventCall, 'o evento é postado no canal dos eventos');
  const signupIds = eventCall.body.components[0].components.map((b) => b.custom_id);
  assert.equal(signupIds.length, 5);
  const eventId = signupIds[0].split(':')[2];
  ok('evento criado pelo /fcc: tipo, formulário, revisão e post pelo bot');

  // Inscrição: vaga, reserva, subir da reserva com DM para quem ligou.
  const person = (id, customId) => ({ type: 3, guild_id: '42', token: 't', member: { permissions: '0', user: { id } }, data: { custom_id: customId, component_type: 2 } });
  const tank1 = await (await interaction(person('1001', `fc:ev:${eventId}:tank`))).json();
  assert.equal(tank1.type, 7);
  assert.match(tank1.data.embeds[0].fields[0].name, /Tank 1\/1/);
  await interaction(person('1002', 'fc:pref:dm'));
  const following = calls.length;
  const tank2 = await (await interaction(person('1002', `fc:ev:${eventId}:tank`))).json();
  assert.match(tank2.data.embeds[0].fields.find((f) => f.name.includes('Reserva')).value, /<@1002>/);
  await waitFor(() => calls.slice(following).some((c) => c.url === '/api/v10/webhooks/7/t' && /reserva/.test(c.body.content)), 'aviso da reserva só para quem clicou');
  const dming = calls.length;
  const leave = await (await interaction(person('1001', `fc:ev:${eventId}:tank`))).json();
  assert.match(leave.data.embeds[0].fields[0].value, /<@1002>/);
  await waitFor(() => calls.slice(dming).some((c) => c.url === '/api/v10/users/@me/channels' && c.body.recipient_id === '1002'), 'DM para quem subiu da reserva');
  await waitFor(() => calls.slice(dming).some((c) => /saiu da reserva/.test(c.body?.content ?? '')), 'texto da DM');
  const myEvents = await (await interaction(person('1002', 'fc:events'))).json();
  assert.match(myEvents.data.embeds[0].description, /Savage de terça.*Tank/);
  ok('inscrição com reserva, subida automática e DM só para quem ligou');

  // Cancelar: só quem criou ou quem gerencia. Avisa no canal quem estava inscrito.
  const notMine = await (await interaction(person('1001', `fc:evx:${eventId}:cancel`))).json();
  assert.match(notMine.data.content, /Só quem criou/);
  const cancelling = calls.length;
  const cancelled = await (await interaction(click(`fc:evx:${eventId}:cancel`, '32'))).json();
  assert.deepEqual(cancelled.data.components, []);
  assert.match(cancelled.data.embeds[0].title, /cancelado/);
  await waitFor(() => calls.slice(cancelling).some((c) => c.url === '/api/v10/channels/500/messages' && /foi cancelado/.test(c.body?.content ?? '')), 'aviso do cancelamento');
  const late = await (await interaction(person('1003', `fc:ev:${eventId}:dps`))).json();
  assert.match(late.data.content, /já fecharam/);
  const afterCancel = await (await fetch(`${service}/state`, { headers: auth })).json();
  assert.deepEqual(afterCancel.events, []);
  ok('cancelar o evento avisa e fecha as inscrições');

  // Ligação por código: quem gerencia gera o código, o plugin confere, confirma e passa a usar o token.
  const linkPost = (route, body, token) => fetch(`${service}/link/${route}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: JSON.stringify(body),
  });
  const FC_ID = '9223372036854775001';
  const codeOf = (reply) => reply.data.embeds[0].description.match(/FCC-[0-9A-Z]{6}/)[0];
  const codeReply = await (await interaction(click('fc:do:code', '32'))).json();
  const code = codeOf(codeReply);
  assert.equal(codeReply.data.flags, 64);
  assert.match(codeReply.data.embeds[0].description, /Plugins ligados: 0/);
  // Abrir de novo mostra o mesmo código.
  assert.equal(codeOf(await (await interaction(click('fc:do:code', '32'))).json()), code);
  assert.match((await (await interaction(click('fc:do:code'))).json()).data.content, /Só quem gerencia/);

  assert.equal((await linkPost('check', { code: 'FCC-ZZZZZZ', fcId: FC_ID, owner: 'dono-0001' })).status, 404);
  assert.equal((await linkPost('check', { code, fcId: 123, owner: 'dono-0001' })).status, 400);
  const checked = await (await linkPost('check', { code: code.toLowerCase().replace('-', ' '), fcId: FC_ID, owner: 'dono-0001' })).json();
  assert.equal(checked.serverName, 'Servidor Teste');
  assert.equal(checked.firstLink, true);
  const confirmed = await (await linkPost('confirm', {
    code, fcId: FC_ID, fcName: 'Maré Alta', owner: 'dono-0001',
    characters: [{ name: 'Lumi Noctis', world: 'Behemoth', fcId: FC_ID, jobs: { healer: 'WHM' } }, { name: 'Alt Secreto', fcId: '77' }],
  })).json();
  assert.match(confirmed.token, /^fco_/);
  assert.equal(confirmed.fcName, 'Maré Alta');
  // Personagem de outra FC não liga com o código desta.
  const otherFc = await linkPost('check', { code, fcId: '77', owner: 'dono-0002' });
  assert.equal(otherFc.status, 409);
  assert.equal((await otherFc.json()).fcName, 'Maré Alta');
  ok('código da FC: gerar, conferir no plugin e confirmar');

  // O token substitui a chave: envio da FC certa entra, de alt é ignorado sem erro.
  const sendAs = (token, body) => fetch(`${service}/update`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const sub = { slot: 2, name: 'Tubarao', returnTime: Math.floor(Date.now() / 1000) + 7200, age: 0 };
  assert.equal((await sendAs(confirmed.token, { fcId: FC_ID, subs: [sub] })).status, 200);
  const alt = await sendAs(confirmed.token, { fcId: '77', subs: [{ ...sub, name: 'DoAlt' }] });
  assert.equal(alt.status, 202);
  assert.equal((await alt.json()).stored, false);
  const viaToken = await (await fetch(`${service}/state`, { headers: { Authorization: `Bearer ${confirmed.token}` } })).json();
  assert.equal(viaToken.subs[2].name, 'Tubarao');
  assert.ok(!JSON.stringify(viaToken).includes('DoAlt'));
  assert.ok(!JSON.stringify(viaToken).includes('Alt Secreto'));
  assert.equal((await fetch(`${service}/logs`, { headers: { Authorization: `Bearer ${confirmed.token}` } })).status, 404);
  assert.equal((await fetch(`${service}/state`, { headers: { Authorization: 'Bearer fco_inventado' } })).status, 401);
  assert.equal((await fetch(`${service}/state`, { headers: auth })).status, 200, 'a chave antiga ainda vale');
  ok('o token do dono substitui a chave, e envio de alt fica de fora');

  // Pedido: quem liga depois do dono fica esperando, e o dono aceita, promove e expulsa pelo plugin.
  assert.equal(confirmed.status, 'member');
  assert.equal(confirmed.role, 'owner');
  const request = await (await linkPost('confirm', {
    code, fcId: FC_ID, owner: 'dono-0004', requester: { name: 'Nova Pessoa', world: 'Behemoth' },
  })).json();
  assert.equal(request.status, 'pending');
  const stateOf = (token) => fetch(`${service}/state`, { headers: { Authorization: `Bearer ${token}` } });
  assert.equal((await stateOf(request.token)).status, 403);
  assert.equal((await sendAs(request.token, { fcId: FC_ID, subs: [sub] })).status, 403);
  assert.equal((await (await linkPost('me', {}, confirmed.token)).json()).pending, 1);
  assert.equal((await linkPost('members', {}, request.token)).status, 403, 'pedido não vê a lista');
  const list = (await (await linkPost('members', {}, confirmed.token)).json()).members;
  const asked = list.find((m) => m.status === 'pending');
  assert.equal(asked.name, 'Nova Pessoa');
  assert.ok(!JSON.stringify(list).includes('dono-0004'), 'a lista não mostra o ID do dono do plugin');
  assert.equal((await linkPost('manage', { action: 'accept', member: asked.id }, request.token)).status, 403);
  assert.equal((await linkPost('manage', { action: 'accept', member: asked.id }, confirmed.token)).status, 200);
  assert.equal((await stateOf(request.token)).status, 200);
  assert.equal((await linkPost('manage', { action: 'promote', member: asked.id }, confirmed.token)).status, 200);
  assert.equal((await (await linkPost('me', {}, request.token)).json()).role, 'admin');
  const self = list.find((m) => m.you);
  assert.equal((await linkPost('manage', { action: 'kick', member: self.id }, request.token)).status, 403, 'admin não expulsa o dono');
  assert.equal((await linkPost('manage', { action: 'kick', member: asked.id }, confirmed.token)).status, 200);
  assert.equal((await stateOf(request.token)).status, 401);
  ok('pedido para entrar, aceitar, promover e expulsar');

  // Ligar a conta do Discord: código só para quem pediu, digitado no jogo.
  const askAccount = await (await interaction(person('1002', 'fc:linkme'))).json();
  const accountCode = askAccount.data.embeds[0].description.match(/\d{6}/)[0];
  assert.equal((await linkPost('account', { code: '000000' }, confirmed.token)).status, 404);
  assert.equal((await linkPost('account', { code: accountCode })).status, 401);
  const linkedAccount = await (await linkPost('account', { code: accountCode }, confirmed.token)).json();
  assert.equal(linkedAccount.discordUser, '1002');
  assert.equal((await linkPost('account', { code: accountCode }, confirmed.token)).status, 404, 'o código vale uma vez');
  ok('ligar a conta do Discord com código digitado no jogo');

  // Código novo: o antigo para de valer, quem já ligou continua. Desligado, ninguém liga.
  const renewed = codeOf(await (await interaction(click('fc:do:code-new', '32'))).json());
  assert.notEqual(renewed, code);
  assert.equal((await linkPost('check', { code, fcId: FC_ID, owner: 'dono-0003' })).status, 404);
  assert.equal((await linkPost('check', { code: renewed, fcId: FC_ID, owner: 'dono-0003' })).status, 200);
  assert.equal((await sendAs(confirmed.token, { fcId: FC_ID, subs: [sub] })).status, 200);
  const off = await (await interaction(click('fc:do:code-off', '32'))).json();
  assert.doesNotMatch(off.data.embeds[0].description, /FCC-/);
  assert.match((await (await interaction(click('fc:do:code', '32'))).json()).data.embeds[0].description, /Código desligado/);
  assert.equal((await linkPost('check', { code: renewed, fcId: FC_ID, owner: 'dono-0003' })).status, 404);
  // Sair: o token para de valer.
  assert.deepEqual(await (await linkPost('leave', {}, confirmed.token)).json(), { ok: true });
  assert.equal((await fetch(`${service}/state`, { headers: { Authorization: `Bearer ${confirmed.token}` } })).status, 401);
  ok('código novo, código desligado e sair da FC');

  // Servidor novo: quem gerencia cria a FC; ela é outra, sem os dados da primeira.
  const elsewhere = (data) => ({ ...data, guild_id: '77' });
  assert.match((await (await interaction(elsewhere(command('fcc')))).json()).data.content, /not linked/);
  const setup = await (await interaction(elsewhere(command('fcc', '32')))).json();
  assert.equal(setup.data.components[0].components[0].custom_id, 'fc:setup');
  const created = await (await interaction(elsewhere(click('fc:setup', '32')))).json();
  const newCode = codeOf(created);
  const second = await (await linkPost('confirm', { code: newCode, fcId: '555000', fcName: 'Ferro Velho', owner: 'dono-0009' })).json();
  const secondState = await (await fetch(`${service}/state`, { headers: { Authorization: `Bearer ${second.token}` } })).json();
  assert.deepEqual(secondState.subs, {});
  const newMenu = await (await interaction(elsewhere(command('fcc')))).json();
  assert.equal(newMenu.type, 4);
  ok('servidor novo cria a própria FC pelo /fcc');

  console.log(`TODOS PASSARAM (${passed})`);
  exitCode = 0;
} catch (error) {
  console.error(`FALHA: ${error.message}`, error.stack?.split("\n").find((l) => l.includes("integration.mjs:")));
} finally {
  discord.close();
  try {
    if (process.platform === 'win32') execSync(`taskkill /PID ${worker.pid} /T /F`, { stdio: 'ignore' });
    else process.kill(-worker.pid, 'SIGTERM');
  } catch {
    // O processo já terminou.
  }
  rmSync('.dev.vars', { force: true });
  try {
    rmSync(persist, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
  } catch {
    console.log(`Aviso: a pasta temporária ${persist} ainda estava em uso e não foi apagada.`);
  }
}

process.exit(exitCode);
