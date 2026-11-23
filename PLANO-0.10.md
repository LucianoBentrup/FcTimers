# Plano da 0.10: casa, lucro e estoque

## Objetivo
Usar melhor os dados que o plugin já pega, sem ler telas novas do jogo: avisar antes de a casa da FC ser derrubada, mostrar o lucro ao longo do tempo, prever quando o estoque acaba e mandar um resumo semanal no Discord.

## Fora desta versão
- **Airships:** a FC só pode ter 4 viagens ao mesmo tempo, somando subs e airships. Os 4 subs ocupam todas, e o loot de airship rende bem menos.
- **Várias FCs no mesmo serviço:** fica para quando outra FC for usar.

## 1. Aviso da casa sem visita
**Regra do jogo:** a casa da FC é derrubada se nenhum membro entrar nela por 45 dias. Junto com a casa vão a oficina e os subs. Só logar no jogo não conta, tem que entrar na casa. O jogo manda um e-mail aos 42 dias, e a Square já suspendeu a derrubada algumas vezes.

**Como o plugin sabe**
- A casa da FC é aprendida sozinha: a oficina dos subs fica dentro dela, e o plugin já guarda o `HouseId` da oficina. Não tem nada para configurar.
- Quando um personagem marcado está dentro de uma casa (`HousingManager.GetCurrentIndoorHouseId()`) e o ID bate com o da oficina, conta como visita. A checagem roda ao trocar de zona e de vez em quando enquanto está lá dentro.
- A hora da visita vai para o serviço com o nome do personagem. Vale a visita mais recente entre os membros.
- Ainda falta confirmar no jogo se entrar num quarto privado dentro da casa da FC também conta. Até lá, só conta a casa.

**O que aparece**
- **Painel:** a linha `🏠 Última visita na casa: há N dias (nome)` aparece a partir de 7 dias.
- **Avisos com ping:** aos 30, 40 e 44 dias, um por marco, com o último em tom de urgência. Somem quando alguém entra.
- **Plugin:** um card na Visão geral com os dias que faltam e um toggle "Avisar sobre a casa" nas configurações.
- **Se a Square suspender a derrubada:** basta desligar o aviso nas configurações.

**Limite:** só conta quem tem o plugin. Se outro membro entrar sem o plugin, o aviso dispara à toa, o que é o lado seguro do erro.

## 2. Lucro ao longo do tempo
**O problema:** hoje o serviço guarda só 7 dias de histórico. O plugin guarda 60 dias de viagens, mas só deste PC.

**No serviço**
- Guardar um resumo por dia: gil por sub, por rota (setores) e número de viagens. Esse resumo é pequeno e fica 120 dias.
- As viagens completas continuam com 7 dias.

**No Discord**
- `/loot` ganha a opção `mes`.
- Novo `/lucro`: as últimas 4 semanas, a melhor rota e o gil por tanque gasto de cada rota.

**No plugin**
- Uma seção nova na aba Loot com o total por semana e por mês.
- Um ranking de rotas por gil por viagem e por tanque.

## 3. Previsão do estoque
- **Gasto por dia:** calculado a partir das viagens dos últimos 7 dias (tanques gastos por rota) e dos reparos feitos.
- **Painel e `/estoque`:** em vez de só "dá para X rodadas", mostram também a data estimada, tipo `⛽ acaba ~05/12`, separada para tanques e kits.
- **Aviso:** quando faltarem 5 dias ou menos para acabar. Ele substitui o aviso de estoque baixo de hoje, que olha só a quantidade.
- **Sem dado suficiente:** com menos de 3 dias de viagens, não mostra a previsão.

## 4. Resumo semanal
- Toda segunda às 09:00 (horário do painel), o serviço manda uma mensagem no canal, sem ping, com:
  - o gil da semana;
  - a comparação com a semana anterior;
  - viagens e reparos feitos;
  - tanques e kits gastos;
  - o sub que mais rendeu;
  - quanto tempo os subs ficaram parados esperando alguém mandar.
- Usa o alarme do Durable Object que já existe. Não se apaga em 8 horas como os avisos.
- Pode ser desligado com `/resumo desligar` (Gerenciar servidor).

## Ordem
1. Resumo por dia no serviço (base do 2, 3 e 4), com testes.
2. Previsão do estoque.
3. Lucro: `/loot mes`, `/lucro` e a seção no plugin.
4. Resumo semanal.
5. Aviso da casa: plugin, serviço, painel e avisos.
6. Changelog, README, REGRAS, testes, e testar no jogo antes de publicar.

Tudo na `dev`, um commit por parte. Só vai para a `main` depois do teste no jogo.

## Riscos
- **Resumo por dia:** só começa a acumular quando a 0.10 for para o ar. Dá para preencher os primeiros 60 dias com as viagens que o plugin guarda localmente, enviando uma vez.
- **Previsão do estoque:** fica errada se mudarem de rota no meio da semana. Ela se ajusta sozinha em alguns dias.
- **ID da casa:** se o ID da oficina mudar, por exemplo porque a FC mudou de casa, o plugin aprende o novo na próxima vez que abrir os subs.
