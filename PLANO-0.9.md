# Plano da 0.9: comandos no Discord

## Objetivo
Trocar os scripts do `tools/` (reenviar painel, reagrupar avisos, baixar logs) por slash commands no próprio Discord, sem chave no terminal e sem bot ligado o tempo todo. É também a base da versão pública com bot.

## Como funciona
- Um app do Discord recebe os comandos por HTTP ("Interactions Endpoint URL"). O Discord manda cada comando para `POST /interactions` no serviço que já existe na Cloudflare.
- O serviço confere a assinatura Ed25519 de cada pedido com a chave pública do app. Pedido sem assinatura válida é recusado com 401, como o Discord exige.
- O app entra no servidor só com o escopo `applications.commands`. Não precisa de bot conectado nem de token guardado no serviço.
- O painel e os avisos continuam saindo pelo webhook atual. Nada muda para quem só lê o canal.

## Comandos
| Comando | O que faz | Quem pode |
|---|---|---|
| `/painel` | Apaga o painel e manda outro no fim do canal (hoje: `reenviar-painel.ps1`). | Gerenciar servidor |
| `/avisos limpar` | Troca avisos repetidos por avisos agrupados, sem notificar (hoje: `reagrupar-avisos.ps1`). | Gerenciar servidor |
| `/loot [dia\|semana]` | Total de loot do dia ou dos últimos 7 dias, por sub. | Todos |
| `/estoque` | Tanques, kits, rodadas e reparos que o estoque paga. | Todos |
| `/subs` | Volta, rank e desgaste de cada sub. | Todos |
| `/log [personagem]` | Último log do plugin como arquivo (hoje: `baixar-logs.ps1`). | Gerenciar servidor |

- As respostas de `/loot`, `/estoque`, `/subs` e `/log` aparecem só para quem pediu (efêmeras), para não encher o canal.
- `/painel` e `/avisos limpar` respondem na hora "Feito" e fazem o trabalho em seguida, porque o Discord só espera 3 segundos.
- A permissão vem do próprio Discord (`default_member_permissions`), então não existe senha nova.

## O que o Luciano faz (uma vez)
1. Criar o app em discord.com/developers e copiar o **Application ID** e a **Public Key**. Os dois não são segredo.
2. Colocar o app no servidor pelo link de convite com o escopo `applications.commands`.
3. Depois do deploy, colar em **Interactions Endpoint URL** o endereço `https://fc-timers.lucianobentrup.workers.dev/interactions`. O Discord testa o endereço na hora.
4. Rodar o script de registro dos comandos e colar o **Client Secret** quando ele pedir. O segredo não fica salvo.

## O que eu faço
1. Rota `POST /interactions` no Worker, fora da checagem da chave da FC, com verificação Ed25519 (`crypto.subtle`) e resposta ao PING do Discord.
2. Os comandos, reaproveitando o que já existe: `handleRepost`, `handleRegroup`, `renderPanel`/`suppliesText`, `voyagesOn` e os logs guardados.
3. `DISCORD_APP_ID` e `DISCORD_PUBLIC_KEY` como variáveis do Worker no `wrangler.toml`.
4. `tools/registrar-comandos.ps1`: pega um token temporário com o Client Secret (client credentials) e registra os comandos do servidor.
5. Testes: assinatura válida e inválida, PING, cada comando com estado de exemplo, permissão.
6. README e REGRAS com os comandos. Os scripts do `tools/` continuam funcionando como reserva.

## Fora desta versão
- Várias FCs e código curto para colar no `/fct`: fica para a versão pública, em cima deste mesmo app.
- Trocar o webhook pelo bot para mandar o painel.
- Painel e avisos em inglês.

## Riscos
- Se a verificação da assinatura falhar, o Discord recusa o endereço e os comandos não aparecem. Os scripts continuam como reserva.
- Comandos de servidor aparecem na hora. Comandos globais levam até 1 hora, então começamos com comandos só do servidor de vocês.
