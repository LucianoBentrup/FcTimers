// Ligação entre a conta do Discord e o jogo. Ainda não decidida (ver plano/INVESTIGACAO-ID-FC-E-DM.md):
// o código pode ir do Discord pro jogo ou o contrário, e a ligação pode ser com o dono do plugin ou com cada
// personagem. Tudo que depende dela passa por aqui, e só aqui muda quando a decisão sair.
//
// O que usa hoje:
//   - a inscrição em evento de grupo, para sugerir o job do personagem na função escolhida;
//   - o aviso de evento dentro do jogo (o plugin precisa saber quais eventos são de quem).
// Enquanto não houver ligação, as duas coisas funcionam sem ela: a inscrição mostra só a função, e o plugin
// recebe os eventos da FC sem saber quem do Discord é quem.

// Personagem ligado a esta conta do Discord, ou null. Formato futuro: { name, jobs: { tank, healer, dps } }.
// eslint-disable-next-line no-unused-vars
export function characterOf(state, discordUserId) {
  return null;
}

// Job sugerido para a função ('tank', 'healer', 'dps'), ou null.
export function suggestedJob(state, discordUserId, role) {
  return characterOf(state, discordUserId)?.jobs?.[role] ?? null;
}
