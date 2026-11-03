// Monta o painel e os avisos de exemplo com o mesmo código do serviço, usando dados fictícios.
// Grava as mensagens na pasta recebida; quem envia ao Discord é o painel-teste.ps1.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  alertPayload, applyUpdate, mergeVoyage, newState, priceVoyage, renderPanel, sectorNotice,
} from '../service/src/logic.js';

const ROLE_ID = '1566210740170399855';
const now = Math.floor(Date.now() / 1000);
const hours = (h) => now + Math.round(h * 3600);

// Os 4 subs na mesma build e rota; Tael com peças gastas para mostrar o aviso de reparo.
const details = (rank, exp, condition) => ({ rank, exp, build: 'S+S+U+C+', route: 'J > O > R > Z', tanks: 21, kits: 24, condition });

const state = newState();
applyUpdate(state, {
  subs: [
    { slot: 0, name: 'Navi', returnTime: hours(3), age: 0, details: details(130, 51, 40.4) },
    { slot: 1, name: 'Ciela', returnTime: hours(3), age: 0, details: details(130, 12, 40.4) },
    { slot: 2, name: 'Tatl', returnTime: hours(0.5), age: 0, details: details(130, 16, 40.4) },
    { slot: 3, name: 'Tael', returnTime: hours(-1), age: 0, details: details(129, 84, 27.5) },
  ],
  buffs: { age: 0, slots: [{ slot: 0, itemId: 1, name: 'Grade 3 Wheel of Confrontation', hours: 70 }] },
  activeBuffs: { age: 0, buffs: [{ name: 'Meat and Mead II', seconds: 86392 }, { name: 'The Heat of Battle II', seconds: 50000 }] },
  // Dois vasos prontos para regar que morrem em 2h30, e um canteiro regado depois.
  plots: [
    { key: 'casa:0:v1', name: 'Vaso 1', nextWaterAt: hours(-21.5), dieAt: hours(2.5), age: 0, indoor: true },
    { key: 'casa:0:v2', name: 'Vaso 2', nextWaterAt: hours(-21.4), dieAt: hours(2.6), age: 0, indoor: true },
    { key: 'fora:c1', name: 'Canteiro 1', nextWaterAt: hours(5), dieAt: hours(29), age: 0 },
  ],
  supplies: { tanks: 150, kits: 65, age: 0 },
}, now);

const item = (itemId, name, count, value, market = false) => ({ itemId, name, count, value, ...(market ? { market } : {}) });
const voyage = (slot, subName, back, items, extra = {}) => mergeVoyage(null, { slot, subName, returnTime: hours(back), world: 'Behemoth', items, ...extra });

// Tatl com um item caro de market e um setor novo.
const tatl = priceVoyage(voyage(2, 'Tatl', -9, [
  item(5, 'Rose Gold Ingot', 19, 1900, true),
  item(6, 'Extravagant Salvaged Earring', 4, 120000),
], { unlocked: ['Deep-sea Site 9'] }), { 5: 20000 });

const today = [
  voyage(0, 'Navi', -5, [item(1, 'Extravagant Salvaged Earring', 5, 150000), item(2, 'Gold Ingot', 20, 100, true)]),
  voyage(1, 'Ciela', -5, [item(3, 'Salvaged Necklace', 5, 65000)]),
  tatl.voyage,
  voyage(3, 'Tael', -1, [item(4, 'Extravagant Salvaged Necklace', 5, 172500)]),
];

const subs = Object.values(state.subs);
const perRound = subs.reduce((sum, s) => sum + s.details.tanks, 0);
const plots = state.plots;
const messages = {
  panel: renderPanel(state, today, now),
  alerts: [
    { kind: 'subReminder', subs: [state.subs[0], state.subs[1]] },
    { kind: 'subReady', subs: [state.subs[3]] },
    { kind: 'subForgotten', subs: [state.subs[3]] },
    { kind: 'wheelReady', wheels: Object.values(state.buffs) },
    { kind: 'plotReminder', plots: [plots['fora:c1']] },
    { kind: 'plotReady', plots: [plots['casa:0:v1'], plots['casa:0:v2']] },
    { kind: 'plotDying', plots: [plots['casa:0:v1'], plots['casa:0:v2']] },
    { kind: 'plotForgotten', plots: [plots['casa:0:v2']] },
    { kind: 'repairNeeded', subs: [state.subs[3]], supplies: state.supplies },
    { kind: 'suppliesLow', supplies: state.supplies, lowTanks: 2 * perRound, perRound, perVoyage: perRound / subs.length },
    { kind: 'marketDrop', notice: tatl.notice },
    { kind: 'sectorUnlocked', notice: sectorNotice(tatl.voyage) },
  ].map((alert) => {
    // No teste, a marcação aparece mas não notifica ninguém.
    const payload = alertPayload(alert, ROLE_ID);
    return { ...payload, content: `[TESTE] ${payload.content}`, allowed_mentions: { parse: [], roles: [] } };
  }),
};

// Grava em arquivos UTF-8 para os emojis não dependerem da codificação do terminal.
const out = process.argv[2];
mkdirSync(out, { recursive: true });
writeFileSync(join(out, 'panel.json'), JSON.stringify(messages.panel), 'utf8');
messages.alerts.forEach((alert, i) => writeFileSync(join(out, `alert-${String(i).padStart(2, '0')}.json`), JSON.stringify(alert), 'utf8'));
