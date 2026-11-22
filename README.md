# FC Companion

[![CI](https://github.com/LucianoBentrup/FcTimers/actions/workflows/ci.yml/badge.svg)](https://github.com/LucianoBentrup/FcTimers/actions/workflows/ci.yml)

Plugin de Dalamud para FFXIV que mostra os timers dos submarinos, da roda de buff e dos buffs da FC, e o loot do dia. Ele só lê e exibe informações, sem automatizar nada no jogo.

É uma ferramenta de terceiros: usar é uma escolha sua, e os termos do jogo valem para ela.

## Instalar
1. No jogo, digite `/xlsettings` e abra a aba **Experimental**.
2. Em **Custom Plugin Repositories**, adicione o link abaixo, clique no **+** e salve:
   ```
   https://raw.githubusercontent.com/LucianoBentrup/FcTimers/main/repo.json
   ```
3. Digite `/xlplugins` e instale o **FC Timers**.

## Configurar
1. Digite `/fct` e marque o seu personagem.
2. Depois de regar a plantação, o plugin tenta registrar sozinho. Cada plot é registrado separado. Se não registrar, use **Reguei todos agora** ou `/fct regar`.
3. Para enviar ao painel da FC e receber o que os outros membros leram, abra a seção **Discord**, cole o endereço do serviço e a chave da FC e marque a opção de enviar. O endereço e a chave são passados pela FC.

## Estrutura
- Raiz: código do plugin.
- `service/`: serviço no Cloudflare que publica o painel no Discord.
- `REGRAS.md`: regras do painel e da sincronia.
- `tools/gerar-repo.ps1`: gera `repo.json` e `dist/latest.zip`.
- `tools/reenviar-painel.ps1`: apaga o painel do Discord e manda outro no fim do canal. Pede a chave da FC.
- `tools/baixar-logs.ps1`: baixa os logs de diagnóstico enviados pelos plugins para `tools/logs`, fora do git. Pede a chave da FC.

## Comandos no Discord (FC Companion)
- `/painel`, `/avisos limpar` e `/log`: só para quem gerencia o servidor.
- `/loot [hoje|semana]`, `/estoque` e `/subs`: para todos. As respostas aparecem para todo o canal, sem marcar ninguém; só o `/log` e as mensagens de erro ficam visíveis só para quem pediu.
- Os comandos chegam em `POST /interactions` no serviço, conferidos pela assinatura do app. `tools/registrar-comandos.ps1` registra ou atualiza a lista (pede o Client Secret, que não fica salvo).

## Log de diagnóstico
- Na janela do `/fct`, seção **Diagnóstico**: **Copiar log** copia para colar no Discord, e **Enviar log** guarda no serviço da FC.
- O plugin envia o log sozinho uns 20 segundos depois de ações importantes (coleta, sub saindo, rega, estoque, peças) e ao fechar o jogo. Ações em sequência entram no mesmo envio.
- O serviço guarda só o último log de cada personagem, até 10 personagens. O log não inclui a chave da FC.

## Desenvolvimento
- `dotnet test tests/FcTimers.Tests`: testes da lógica do plugin (junção dos dados, loot, migração do config, nomes dos campos), sem o jogo e sem o Dalamud.
- `npm test` em `service/`: testes do serviço. `npm run test:integration` sobe o serviço localmente com um Discord simulado (só no Windows).
- **CI:** a cada envio para `main` ou `dev`, o GitHub roda os testes do serviço e do plugin e compila o plugin com o Dalamud mais recente.
- **Publicar uma versão:** subir a versão no `FcTimers.csproj`, juntar no `main` e criar a tag `vX.Y.Z` no último commit do `main`. O GitHub compila, grava `repo.json` e `dist/latest.zip` no `main` e cria a release. O `tools/gerar-repo.ps1` continua funcionando para publicar na mão.
