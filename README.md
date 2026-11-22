# FC Companion

[![CI](https://github.com/LucianoBentrup/FcTimers/actions/workflows/ci.yml/badge.svg)](https://github.com/LucianoBentrup/FcTimers/actions/workflows/ci.yml)

Plugin de Dalamud para FFXIV que acompanha a Free Company: submarinos, loot, estoque da oficina, plantação e buffs. Um serviço na Cloudflare junta o que cada membro leu e mantém um painel compartilhado no Discord, com avisos e comandos.

O plugin **só lê o que o jogo já mostra**. Ele não automatiza nada, não manda nada para os servidores do jogo e não abre telas sozinho. É uma ferramenta de terceiros: usar é uma escolha sua, e os termos do jogo valem para ela.

## O que faz

**Submarinos**
- Horário de volta, rank, XP, peças (build), rota e desgaste de cada sub.
- Loot de cada viagem, salvo no instante em que o resultado chega (rápido o bastante para o AutoRetainer). Se uma captura falhar, abrir o sub na oficina recupera o loot.
- Cada item conta pelo maior valor entre a venda para NPC e o market (média das vendas recentes no Universalis, no mundo do personagem ou no escolhido).

**Estoque**
- Tanques de ceruleum e kits de reparo do baú da FC e do inventário, com quantas rodadas e quantos reparos dos subs eles pagam.

**Plantação**
- Rega de cada canteiro ou vaso, detectada pela mensagem do jogo, com o horário de regar de novo e de quando a planta morre.
- Seção **Campos**: selecione o canteiro ou vaso no jogo, clique em **+** e dê um nome. Nomes e remoções valem para todos os membros.

**Buffs**
- Mesa da roda (quando cada buff fica pronto) e buffs ativos da FC.

**Discord** (com o serviço)
- Um painel único, sempre editado, com tudo acima e o loot do dia.
- Avisos marcando o cargo da FC: lembrete 1 hora antes, sub pronto, sub esquecido, rega, planta perto de morrer, reparo, estoque baixo, item caro de market, setor novo e buff da mesa pronto. Avisos que acontecem juntos saem numa mensagem só, e todos se apagam depois de 8 horas.
- Comandos do app FC Companion: `/subs`, `/estoque`, `/loot [hoje|semana]` para todos; `/painel`, `/avisos limpar` e `/log` para quem gerencia o servidor.
- O rodapé do painel avisa quando o Dalamud ainda não saiu para um patch novo do jogo.

**No jogo**
- Janela em abas (`/fct`): Submarinos, Loot, Buffs, Plantação e Configurações, com tabelas e cores (verde pronto, amarelo em breve, vermelho urgente).
- Aviso no chat e resumo na barra do jogo. Textos em português e inglês.

## Instalar
1. No jogo, digite `/xlsettings` e abra a aba **Experimental**.
2. Em **Custom Plugin Repositories**, adicione o link abaixo, clique no **+** e salve:
   ```
   https://raw.githubusercontent.com/LucianoBentrup/FcTimers/main/repo.json
   ```
3. Digite `/xlplugins` e instale o **FC Companion**.

## Configurar
1. Digite `/fct`, vá em **Configurações** e marque o seu personagem. Só personagens marcados leem e enviam dados.
2. Para o painel no Discord, ainda em **Configurações**, cole o endereço do serviço e a chave da FC e marque **Enviar e receber dados**. O endereço e a chave são passados pela FC.
3. Para os dados aparecerem: abra a lista de subs na oficina, a aba do baú onde ficam os tanques e a mesa da roda. A rega é registrada sozinha quando você cuida da planta.

## Privacidade
Com o envio ligado, o plugin manda ao serviço da FC: horários, rank, peças e loot dos subs, estoque, rega dos campos, buffs e o log de diagnóstico do plugin. O log tem nomes de personagens e mensagens do sistema do jogo, **nunca** chat de jogadores nem a chave da FC. O serviço guarda o estado atual, o loot dos últimos dias e o último log de cada personagem. Sem o envio ligado, nada sai do PC.

## Estrutura
| Pasta / arquivo | O que é |
|---|---|
| Raiz (`*.cs`) | Plugin. `Plugin.cs` liga tudo; `FcView.cs` junta os dados deste PC com os do serviço; `MainWindow.cs` é a janela. |
| `service/` | Serviço na Cloudflare (Worker + Durable Object): recebe os dados, monta o painel, manda os avisos e responde aos comandos do Discord. |
| `tests/` | Testes da lógica do plugin, sem o jogo. |
| `tools/` | Scripts: publicar na mão, reenviar o painel, agrupar avisos, baixar logs, registrar os comandos e o painel de teste. Os que precisam de chave pedem no terminal e não guardam. |
| `REGRAS.md` | Regras do painel, dos avisos e da sincronia. |
| `CHECKLIST.md`, `PLANO-*.md` | O que está feito, o que falta e o plano de cada versão. |

## Desenvolvimento
- **Plugin:** `dotnet build -c Release` (precisa do Dalamud instalado) e `dotnet test tests/FcTimers.Tests`.
- **Serviço:** em `service/`, `npm test` e `npm run test:integration` (sobe o serviço localmente com um Discord simulado; só no Windows). Publicar: `npx wrangler deploy`.
- **CI:** a cada envio para `main` ou `dev`, o GitHub roda os testes do serviço e do plugin e compila o plugin com o Dalamud mais recente.
- **Publicar uma versão:** subir a versão no `FcTimers.csproj`, escrever a entrada no `Changelog.cs`, juntar no `main` e criar a tag `vX.Y.Z` no último commit do `main`. O GitHub compila, grava `repo.json` e `dist/latest.zip` no `main` e cria a release. O `tools/gerar-repo.ps1` faz o mesmo na mão.
- O trabalho novo vai para o branch `dev` e entra no `main` junto com a versão.

## Próximos passos
- **0.10:** serviço para várias FCs.
- **0.11:** o app do Discord vira bot, manda o painel no lugar do webhook e ganha um cadastro fácil com código.
- **0.12:** painel em inglês e página de privacidade.
- **1.0:** versão pública, distribuída por este repositório.
