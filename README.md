# FC Timers

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

## Log de diagnóstico
- Na janela do `/fct`, seção **Diagnóstico**: **Copiar log** copia para colar no Discord, e **Enviar log** guarda no serviço da FC.
- O plugin envia o log sozinho uns 20 segundos depois de ações importantes (coleta, sub saindo, rega, estoque, peças) e ao fechar o jogo. Ações em sequência entram no mesmo envio.
- O serviço guarda só o último log de cada personagem, até 10 personagens. O log não inclui a chave da FC.
