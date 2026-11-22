// Comandos do Discord recebidos por HTTP (Interactions Endpoint). Sem bot conectado: o Discord manda cada
// comando para POST /interactions, assinado com a chave do app. Ver ../../PLANO-0.9.md.

export const PING = 1;
export const COMMAND = 2;
export const REPLY = 4;
export const DEFERRED_REPLY = 5;
export const EPHEMERAL = 64;
// Permissão "Gerenciar servidor" do Discord.
export const MANAGE_GUILD = 0x20n;

const hexToBytes = (hex) => Uint8Array.from(hex.match(/.{2}/g) ?? [], (byte) => parseInt(byte, 16));

// O Discord exige recusar com 401 qualquer pedido cuja assinatura Ed25519 não confira.
export async function verifyDiscord(publicKeyHex, signatureHex, timestamp, body) {
  if (!publicKeyHex || !signatureHex || !timestamp || !/^[0-9a-f]+$/i.test(signatureHex)) return false;
  try {
    const key = await crypto.subtle.importKey('raw', hexToBytes(publicKeyHex), { name: 'Ed25519' }, false, ['verify']);
    return await crypto.subtle.verify('Ed25519', key, hexToBytes(signatureHex), new TextEncoder().encode(timestamp + body));
  } catch {
    return false;
  }
}

export const canManage = (interaction) => (BigInt(interaction.member?.permissions ?? '0') & MANAGE_GUILD) === MANAGE_GUILD;

// Resposta que só quem usou o comando vê: erros, falta de permissão e o log.
export const privateReply = (content, extra = {}) => ({
  type: REPLY,
  data: { content, flags: EPHEMERAL, allowed_mentions: { parse: [] }, ...extra },
});

// Resposta que todo mundo no canal vê, sem marcar ninguém.
export const publicReply = (content) => ({ type: REPLY, data: { content, allowed_mentions: { parse: [] } } });

// Nome do comando com o subcomando, se houver: "avisos limpar".
export function commandPath(interaction) {
  const parts = [interaction.data?.name];
  for (let option = interaction.data?.options?.[0]; option && option.type <= 2; option = option.options?.[0]) parts.push(option.name);
  return parts.filter(Boolean).join(' ');
}

export function optionValue(interaction, name) {
  const find = (options) => {
    for (const option of options ?? []) {
      if (option.name === name && option.value !== undefined) return option.value;
      const inner = find(option.options);
      if (inner !== undefined) return inner;
    }
    return undefined;
  };
  return find(interaction.data?.options);
}

// Comandos registrados no servidor pelo tools/registrar-comandos.ps1.
export const COMMANDS = [
  { name: 'painel', description: 'Apaga o painel e manda outro no fim do canal', default_member_permissions: '32' },
  {
    name: 'avisos',
    description: 'Avisos do FC Companion',
    default_member_permissions: '32',
    options: [{ type: 1, name: 'limpar', description: 'Troca avisos repetidos por avisos agrupados, sem notificar' }],
  },
  {
    name: 'loot',
    description: 'Loot dos submarinos',
    options: [{
      type: 3,
      name: 'periodo',
      description: 'Hoje ou os últimos 7 dias',
      choices: [{ name: 'hoje', value: 'dia' }, { name: 'semana', value: 'semana' }],
    }],
  },
  { name: 'estoque', description: 'Tanques, kits e quantas rodadas e reparos eles pagam' },
  { name: 'subs', description: 'Volta, rank e desgaste de cada submarino' },
  {
    name: 'log',
    description: 'Último log do plugin, como arquivo',
    default_member_permissions: '32',
    options: [{ type: 3, name: 'personagem', description: 'Nome do personagem (padrão: o log mais recente)' }],
  },
];
