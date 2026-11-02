// Monta o painel e os avisos de exemplo com o mesmo código do serviço, usando dados fictícios.
// Grava as mensagens na pasta recebida; quem envia ao Discord é o painel-teste.ps1.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  alertPayload, applyUpdate, newState, renderPanel,
} from '../service/src/logic.js';

const ROLE_ID = '1566210740170399855';
const now = Math.floor(Date.now() / 1000);
const hours = (h) => now + Math.round(h * 3600);

const state = newState();
applyUpdate(state, {
  subs: [
    { slot: 0, name: 'Navi', returnTime: hours(3), age: 0 },
    { slot: 1, name: 'Ciela', returnTime: hours(3), age: 0 },
    { slot: 2, name: 'Tatl', returnTime: hours(0.5), age: 0 },
    { slot: 3, name: 'Tael', returnTime: hours(-1), age: 0 },
  ],
  buffs: { age: 0, slots: [{ slot: 0, itemId: 1, name: 'Grade 3 Wheel of Confrontation', hours: 70 }] },
  activeBuffs: { age: 0, buffs: [{ name: 'Meat and Mead II', seconds: 86392 }, { name: 'The Heat of Battle II', seconds: 50000 }] },
  // Dois vasos prontos para regar que morrem em 2h30, e um canteiro regado depois.
  plots: [
    { key: 'v1', name: 'Vaso 1', nextWaterAt: hours(-21.5), dieAt: hours(2.5), age: 0 },
    { key: 'v2', name: 'Vaso 2', nextWaterAt: hours(-21.4), dieAt: hours(2.6), age: 0 },
    { key: 'c1', name: 'Canteiro 1', nextWaterAt: hours(5), dieAt: hours(29), age: 0 },
  ],
}, now);

const item = (itemId, name, count, value) => ({ itemId, name, count, value });
const today = [
  { slot: 0, subName: 'Navi', returnTime: hours(-5), items: [item(1, 'Extravagant Salvaged Earring', 9, 270000), item(2, 'Gold Ingot', 20, 100)] },
  { slot: 1, subName: 'Ciela', returnTime: hours(-5), items: [item(3, 'Salvaged Necklace', 5, 65000)] },
  { slot: 3, subName: 'Tael', returnTime: hours(-1), items: [item(4, 'Extravagant Salvaged Necklace', 5, 172500)] },
];

const subsSoon = [state.subs[0], state.subs[1]];
const messages = {
  panel: renderPanel(state, today, now),
  alerts: [
    alertPayload({ kind: 'subReminder', subs: subsSoon }, ROLE_ID),
    alertPayload({ kind: 'subReady', subs: [state.subs[3]] }, ROLE_ID),
    alertPayload({ kind: 'plotReminder', plots: [state.plots.c1] }, ROLE_ID),
    alertPayload({ kind: 'plotReady', plots: [state.plots.v1, state.plots.v2] }, ROLE_ID),
    alertPayload({ kind: 'plotDying', plots: [state.plots.v1, state.plots.v2] }, ROLE_ID),
  ].map((alert) => ({
    // No teste, a marcação aparece mas não notifica ninguém.
    ...alert,
    content: `[TESTE] ${alert.content}`,
    allowed_mentions: { parse: [], roles: [] },
  })),
};

// Grava em arquivos UTF-8 para os emojis não dependerem da codificação do terminal.
const out = process.argv[2];
mkdirSync(out, { recursive: true });
writeFileSync(join(out, 'panel.json'), JSON.stringify(messages.panel), 'utf8');
messages.alerts.forEach((alert, i) => writeFileSync(join(out, `alert-${i}.json`), JSON.stringify(alert), 'utf8'));
