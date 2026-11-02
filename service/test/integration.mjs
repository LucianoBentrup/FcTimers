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
    const id = req.url.match(/messages\/(\d+)/)?.[1];
    if (req.method === 'DELETE') {
      messages.delete(id);
      calls.push({ method: 'DELETE', messageId: id });
      res.writeHead(204).end();
      return;
    }
    if (req.method === 'PATCH' && !messages.has(id)) {
      calls.push({ method: 'PATCH', missing: true });
      res.writeHead(404).end();
      return;
    }
    const messageId = id ?? String(nextId++);
    messages.set(messageId, JSON.parse(data));
    calls.push({ method: req.method, messageId });
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
writeFileSync('.dev.vars', `DISCORD_WEBHOOK=http://127.0.0.1:${DISCORD_PORT}/api/webhooks/1/abc\nFC_KEY=${KEY}\nROLE_ID=999\n`);
await new Promise((resolve) => { discord.listen(DISCORD_PORT, '127.0.0.1', resolve); });

const worker = spawn(`npx wrangler dev --port ${SERVICE_PORT} --persist-to "${persist}" --log-level error`, { shell: true, stdio: 'ignore' });

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

  console.log(`TODOS PASSARAM (${passed})`);
  exitCode = 0;
} catch (error) {
  console.error(`FALHA: ${error.message}`);
} finally {
  discord.close();
  try {
    execSync(`taskkill /PID ${worker.pid} /T /F`, { stdio: 'ignore' });
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
