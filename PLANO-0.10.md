# Plano da 0.10: casa, airships, lucro e estoque

## Objetivo
Usar melhor os dados que o plugin já pega: avisar antes de a casa da FC ser derrubada, acompanhar as airships, mostrar o lucro ao longo do tempo, prever quando o estoque acaba e mandar um resumo semanal no Discord.

## Fora desta versão
- **Várias FCs no mesmo serviço:** fica para a 0.11, quando outra FC for usar.

## 1. Aviso da casa sem visita (feito)
- **Regra do jogo:** a casa da FC é derrubada depois de 45 dias sem nenhum membro entrar. Só logar no jogo não conta. O jogo manda e-mail aos 42 dias, e a Square já suspendeu a derrubada algumas vezes.
- **Como o plugin reconhece a casa:** pela oficina, que fica dentro dela. Guarda mundo, ward e plot (`Config.FcHouse`).
- **Visita:** um personagem ativo dentro dessa casa registra a entrada (de 10 em 10 minutos no máximo) e manda ao serviço com o nome. Vale a mais recente entre os membros.
- **Painel:** a linha `🏠 Última visita na casa` aparece a partir de 7 dias.
- **Avisos com ping:** aos 30, 40 e 44 dias.
- **Plugin:** card "Casa da FC" na Visão geral, com a barra até os 45 dias, e o toggle de aviso nas configurações.
- **Conferir no jogo:**
  - se a casa é reconhecida, com o log detalhado ligado: "Dentro da casa ... (casa da FC: ...)";
  - se entrar num quarto privado também conta.

## 2. Airships (feito)
- O plugin lê na oficina o nome, a volta, o rank, o XP e o loot de cada airship. O jogo não guarda a rota nem o desgaste delas como faz com os subs.
- O loot entra no total do dia e no lucro. A chave da viagem separa sub e airship na mesma posição.
- Painel com a seção ✈️ Airships (só aparece se existir alguma), aviso "✈️ ... voltou!" e a tabela na aba Submarinos.
- **Conferir no jogo:** se o loot da airship é salvo quando o resultado da viagem chega.

## 3. Lucro ao longo do tempo (feito)
- O serviço guarda as viagens por 120 dias e apaga as mais antigas uma vez por dia.
- Cada viagem de sub guarda a rota e os tanques que o sub tinha quando o loot chegou.
- `/loot mes`, `/lucro` (4 semanas e ranking das rotas por gil por tanque) e a seção Lucro na aba Loot.

## 4. Previsão do estoque (feito)
- O serviço guarda as contagens de 14 dias e soma só as quedas dos últimos 7.
- Com 3 dias de contagens, mostra "acaba ~dd/mm" no painel, no `/estoque` e no plugin.
- O aviso de estoque baixo passa a sair 5 dias antes de acabar.

## 5. Resumo semanal (feito)
- Toda segunda às 9h, sem ping: gil da semana e comparação com a anterior, quem mais rendeu, tanques e kits gastos e o tempo que os subs ficaram parados.
- Começa na segunda seguinte à atualização do serviço.
- `/resumo ligar` e `/resumo desligar`.

## Para publicar
1. Testar no jogo: casa, airships (se a FC tiver), previsão e lucro.
2. Deploy do serviço e rodar `tools/registrar-comandos.ps1` de novo, para aparecer `/lucro`, `/resumo` e a opção mês no `/loot`.
3. Tag `v0.10.0`.

## Depois da 0.10: membros que não usam o plugin
Hoje tudo depende de alguém com o plugin ver a coisa acontecer. As ideias abaixo cobrem esse buraco sem automatizar nada no jogo.

**Casa**
- `/casa visitei` no Discord, para quem entrou sem o plugin registrar a visita. É o que mais vale: o aviso da casa é o único que pode sair errado por falta do plugin.

**Subs**
- Quando um sub muda de horário (saiu de novo) sem nenhum membro com plugin ter visto o loot, o painel marca "loot não registrado" naquela viagem, em vez de o dia ficar com menos gil sem explicação.
- `/subs` mostra há quanto tempo cada sub foi lido, para ficar claro quando o dado está velho.

**Plantação**
- `/regar` no Discord (com autocomplete dos campos cadastrados), para quem regou sem o plugin registrar a rega e o aviso não sair à toa.
- O aviso de rega diz "regado por alguém sem o plugin?" quando o horário passou e ninguém leu.

**Estoque**
- `/estoque comprei 100 tanques`: soma no estoque até a próxima contagem de verdade, para a previsão não ficar errada quando alguém sem plugin compra e guarda no baú.

**Buffs**
- Só o plugin lê o Wheel Stand. Uma linha "lido há X" no painel, como já existe nos buffs ativos, deixa claro quando ninguém olhou.

**Em geral**
- Uma linha discreta no rodapé do painel: "Dados de: Luciano, Amigo" com quem mandou algo nas últimas 24 horas, para a FC saber quem está cobrindo o quê.
- Os comandos de registro manual valem sempre menos que uma leitura do plugin, e o painel mostra ✍️ no que veio do Discord.
