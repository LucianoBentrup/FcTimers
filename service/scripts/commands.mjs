// Gera tools/comandos.json a partir de src/menu.js. Rodar com `npm run commands` depois de mudar o /fcc.
import { writeFileSync } from 'node:fs';
import { COMMANDS } from '../src/menu.js';

writeFileSync(new URL('../../tools/comandos.json', import.meta.url), `${JSON.stringify(COMMANDS, null, 2)}\n`);
console.log(`tools/comandos.json: ${COMMANDS.map((c) => `/${c.name}`).join(', ')}`);
