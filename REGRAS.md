# Regras do painel e da sincronia

## Painel
- Existe uma única mensagem no Discord, sempre editada.
- Ela mostra o status atual, a plantação e o total do loot do dia atual.
- A lista de itens do dia fica fora do painel, para a mensagem ficar leve. O código continua no serviço (`SHOW_ITEMS`) para um bot no futuro.

## Avisos
- Todo aviso marca o cargo @Subs e é apagado 8 horas depois de enviado.
- 1 hora antes de um submarino voltar: "Lembrete: Em 1 hora os submarinos estarão prontos para coleta", com os nomes. Quem volta até 15 minutos depois entra no mesmo lembrete.
- Se faltar menos de 30 minutos quando o serviço puder enviar o lembrete, ele não sai, e só sai o aviso de pronto.
- Quando um submarino volta, sai o aviso de pronto. Submarinos que voltam até 15 minutos um do outro entram no mesmo aviso, que sai quando o último voltar. O lembrete de esquecido segue a mesma regra.
- Plantação, por plot: lembrete 1 hora antes de poder regar, aviso quando pode regar, e um aviso 3, 2 e 1 hora antes de morrer.
- Cada viagem e cada rega avisa uma vez só. Algo visto pela primeira vez quando já estava pronto não gera aviso.
- Quando um buff da mesa da roda fica pronto, sai um aviso marcando @Subs, uma vez só. Buff visto pela primeira vez já pronto não gera aviso.
- A cada 30 minutos o serviço compara a versão atual do jogo (Thaliak) com a que o Dalamud suporta (goatcorp). Enquanto o Dalamud não suporta o patch, o painel mostra "🧩 ⚠️ Dalamud ainda desatualizado", sem aviso nem marcação.
- Lembrete de esquecido: um sub que voltou há mais de 2 horas e não saiu de novo ganha um lembrete, uma vez por viagem. Um vaso de dentro de casa ganha um lembrete quando outro vaso da mesma casa foi regado há 15 minutos ou mais e ele, que já podia ser regado, não foi.
- Nos avisos de plantação, só os vasos de dentro de casa ou de quarto privado aparecem pelo nome. Os canteiros de fora são regados juntos, então aparecem como "a plantação" ou "os canteiros de fora".
- O aviso de estoque baixo diz quantas viagens e rodadas os tanques ainda aguentam.
- O aviso de reparo espera 5 minutos sem leitura nova de sub gasto, para sair um aviso só com todos os subs, e diz quantos kits o reparo de todos gasta. O estoque conta os reparos pelo total dos subs juntos.
- Reparo: quando a peça mais gasta de um sub fica abaixo de 30%, sai um aviso com os subs gastos e os kits que o reparo completo gasta. Ele volta a avisar depois de um reparo.

## Loot do dia
- Só valem as posições de 0 a 3. A 5ª posição da lista do jogo repete um dos 4 submarinos e é ignorada no plugin e no serviço.
- Cada viagem é identificada pelo slot do submarino e pelo horário de retorno.
- O total do dia é sempre recalculado somando todas as viagens daquele dia. Nada zera entre coletas.
- Reler ou reenviar a mesma viagem nunca soma de novo.
- A viagem pertence ao dia em que o submarino voltou, no fuso de Brasília, no painel e na janela do plugin.
- À meia-noite o painel passa a mostrar o novo dia. O serviço guarda as viagens por 120 dias, para o lucro por semana e por mês, e apaga as mais antigas uma vez por dia.
- A viagem de um sub guarda a rota e os tanques que o sub tinha quando o loot chegou. É isso que permite comparar as rotas na seção Lucro do `/fcc`.

## Lucro
- A seção Lucro do `/fcc` e a seção Lucro da aba Loot mostram as últimas 4 semanas (blocos de 7 dias terminando hoje) e as rotas dos subs ordenadas por gil por tanque.
- Viagens de antes da 0.10 não têm rota, então entram nas semanas mas não no ranking das rotas.

## Resumo semanal
- Toda segunda às 9h de Brasília sai uma mensagem no canal, sem marcar ninguém, que não é apagada: gil da semana anterior (segunda a domingo), comparação com a semana antes dela, quem mais rendeu, tanques e kits gastos e o tempo que os subs ficaram prontos esperando sair de novo.
- Na primeira segunda depois de atualizar o serviço, ele só marca a semana. O primeiro resumo sai na segunda seguinte.
- Se o serviço ficar mais de 1 dia sem rodar depois das 9h de segunda, aquela semana é pulada.
- `/resumo desligar` e `/resumo ligar` (Gerenciar servidor) desligam e ligam o resumo.
- O tempo parado é contado da volta do sub até a leitura que viu a viagem nova. Se ninguém com o plugin estava na oficina quando o sub saiu, ele conta a mais. Cada espera conta no máximo 7 dias.

## Airships
- O plugin lê na oficina o nome, a volta, o rank, o XP e o loot de cada airship, como faz com os subs. O jogo não guarda a rota nem o desgaste das airships do mesmo jeito, então eles não aparecem.
- A FC só pode ter 4 viagens ao mesmo tempo, somando subs e airships.
- O loot das airships entra no total do dia e no lucro, com ✈️ na frente do nome. Sub e airship na mesma posição são viagens diferentes.
- Quando uma airship volta, sai o aviso "✈️ ... voltou!", com o mesmo agrupamento de 15 minutos dos subs.

## Casa da FC
- O jogo derruba a casa da FC depois de 45 dias sem nenhum membro entrar nela.
- O plugin aprende qual é a casa da FC pela oficina, que fica dentro dela: mundo, ward e plot.
- Também dá para marcar à mão: dentro de uma casa que não é a da FC, o card "Casa da FC" da Visão geral mostra o botão "Esta é a casa da FC". A casa marcada à mão vale mais que a da oficina, que não troca mais a casa depois disso. Marcar outra casa pelo botão troca de novo.
- Toda vez que um personagem ativo está dentro dessa casa, a entrada é registrada (no máximo de 10 em 10 minutos) e vai ao serviço com o nome do personagem. Vale a entrada mais recente entre os membros.
- O painel mostra "🏠 Última visita na casa" a partir de 7 dias, com ⚠️ a partir de 30.
- Avisos marcando @Subs aos 30, 40 e 44 dias, um por marco. O de 44 dias sai com 🚨. Uma entrada nova recomeça a contagem.
- Só conta quem usa o plugin. Um membro que entra sem o plugin não zera a contagem, então o aviso pode sair à toa, mas nunca deixa de sair.

## Buffs ativos
- Os status do personagem dizem quais buffs estão ativos, e o valor do status indica o nível.
- O tempo só é carregado pelo jogo quando alguém abre a aba de ações. Depois de lido uma vez, o horário de término fica guardado no plugin e no serviço.
- Se o buff surgir com o plugin ligado há pelo menos 1 minuto, o término é estimado em 24 horas. A aba de ações, quando aberta, traz o valor exato.
- Sem horário conhecido, o painel mostra `?` e o horário da leitura. O nível ausente fica só com o nome-base.

## Plantação
- Na aba Plantação do `/fct`, a seção **Campos** cadastra um canteiro ou vaso: selecione o objeto no jogo e clique em **+** (fica cinza sem um alvo válido ou se o objeto já estiver na lista), dê o nome e salve. A seção só cadastra, renomeia e remove; o estado da rega fica em cima.
- O nome dado na janela vale para todos os PCs e nunca é trocado pelo nome automático. Remover um campo tira ele de todos os PCs; uma rega antiga de outro PC não traz ele de volta. Campo cadastrado e ainda não regado não aparece no painel.
- Os canteiros de fora não têm nome no alvo do jogo; o nome vem do menu ("6th Bed, 2nd Patch" vira "Canteiro 2"). Um nome dado na janela do `/fct` não é trocado.
- A posição na chave sai sempre com ponto, para a chave ser igual em todos os PCs. Chaves antigas com vírgula viram uma só, ficando a rega mais recente.
- O plot geral "Plantação" some do painel e da janela quando algum canteiro identificado é regado depois dele.
- Cada plot ou vaso tem o seu horário. Ele é identificado pela casa (território, ward e plot), pelo quarto privado quando houver e pela posição do objeto, que só muda se alguém mover o plot.
- O plugin registra a rega quando o jogo mostra "This crop is doing well." (mensagem 4017, canteiros de fora), "The crops appear to be healthy." (3450, vasos de dentro) ou ao plantar (4015), no plot que estiver selecionado. Sem plot selecionado, a rega vai para o plot geral "Plantação". Ainda falta confirmar no jogo que essas mensagens só aparecem nesses momentos.
- Na mão, "Reguei todos agora" ou `/fct regar` registram a rega em todos os plots conhecidos.
- O próximo horário de regar é 24 horas depois da última rega de cada plot, e o de morrer, 48 horas. Pela wiki, a planta precisa de rega 1 vez por dia; sem rega, as que murcham mais rápido ficam roxas em 24 horas e morrem 24 horas depois. Outras sementes só murcham em 36 ou 48 horas, então o aviso pode chegar antes do necessário, nunca depois.
- Em cada plot, a rega mais recente entre os membros vale, e a de um PC nunca passa por cima de uma mais nova de outro.
- No painel, cada plot tem a sua linha, com o próprio horário. Os avisos juntam os plots que ficam prontos juntos.
- Um plot que morreu há mais de 2 dias some do painel e dos avisos.

## Detalhes dos subs
- Ao abrir a lista de subs na oficina, o plugin lê de cada sub: rank, XP, peças, rota, tanques que a rota gasta e kits de um reparo completo.
- O desgaste das peças é carregado ao entrar na oficina. Vale a peça mais gasta de cada sub.
- No painel, o rank fica ao lado do nome, por exemplo "**Navi** (R130)". Build e rota ficam numa linha discreta no topo quando os 4 subs são iguais; se não forem, cada sub mostra a sua.
- O desgaste aparece no painel abaixo de 50%, com ⚠️ abaixo de 30%.
- Um plugin de versão antiga, que não manda esses dados, não apaga os últimos conhecidos.

## Estoque
- O plugin conta os tanques de ceruleum (item 10155) e os kits de reparo (Magitek Repair Materials, item 10373) no baú da FC e no inventário de quem está jogando.
- O jogo só carrega o baú da FC quando alguém abre. Cada página guarda a última contagem vista, e o total soma as páginas e o inventário.
- A contagem vale a partir de quando mudou. Uma contagem mais antiga nunca passa por cima de uma mais nova de outro membro.
- O painel mostra o estoque só depois da primeira contagem, com um ⚠️ no que estiver baixo.
- Com os detalhes dos subs, o estoque mostra quantas rodadas os tanques aguentam (tanques ÷ soma dos tanques de cada rota) e quantos reparos completos os kits pagam.
- Com menos de 2 rodadas de tanques (ou 120 tanques, enquanto as rotas não são conhecidas) ou 20 kits, sai um aviso marcando @Subs, uma vez só. Ele volta a avisar depois que o estoque for reposto e cair de novo.
- Previsão: o serviço guarda as contagens dos últimos 14 dias e soma só as quedas dos últimos 7 (comprar ou repor não conta como gasto). Com pelo menos 3 dias de contagens, o painel, a seção Estoque do `/fcc` e o plugin mostram "acaba ~dd/mm" para tanques e kits.
- Com a previsão, o aviso de estoque baixo sai quando faltam 5 dias ou menos para acabar, no lugar dos limites fixos.

## Market
- O plugin marca os itens do loot que podem ir para o market e manda junto o mundo dos preços: o mundo de origem do personagem, ou o escolhido em Configurações (mundos do mesmo data center ou o data center inteiro).
- O serviço busca no Universalis a média das vendas recentes de cada item e guarda o preço por 6 horas. O preço fica gravado na viagem, então não muda depois.
- Cada item conta pelo maior valor entre o NPC e o market. O total do dia mostra "≈" e quanto vem do market quando algum item conta pelo market.
- Item de market que vale 100 mil gil ou mais gera um aviso 💎, uma vez por viagem.
- Se o Universalis falhar, o painel sai normalmente e o preço é buscado de novo na próxima atualização.

## Setor novo
- Quando uma viagem desbloqueia um setor, sai um aviso 🗺️ com o nome do setor. Viagens com mais de 1 dia não geram aviso.

## Loot rápido
- O jogo guarda na memória o loot da última viagem coletada de cada sub. O plugin salva o loot quando ele muda, lendo a cada frame dentro da oficina.
- Se o sub já saiu de novo quando o loot chega, a viagem usa a volta anterior.
- Na primeira leitura depois de atualizar, um loot igual a uma viagem já salva não é salvo de novo.

## Personagens
- Cada personagem entra na lista do plugin na primeira vez que loga, desmarcado.
- Só um personagem marcado lê dados do jogo e os envia ao serviço.
- O plugin ignora leituras em duty ou trocando de área, e só aceita uma lista que ficou igual por 5 segundos.
- Um buff com tempo sai sozinho do painel quando acaba. Um buff ativado com todos offline só aparece na próxima leitura.

## Sincronia entre membros
- O plugin envia o que leu e também recebe o estado do serviço: a cada 1 minuto com a janela aberta, e a cada 5 minutos com ela fechada.
- A janela, a barra do jogo e os avisos no chat juntam o que o PC leu com o que os outros enviaram. Vale a leitura mais recente.
- Sem o serviço, o plugin continua mostrando os dados do próprio PC.
- O plugin só envia o que leu do jogo, nunca o que recebeu de outro membro.
- Toda alteração no serviço passa por um único ponto, uma de cada vez, para um envio não sobrescrever o outro.
- A mesma viagem enviada por 2 pessoas vira um único registro.
- Se 2 leituras da mesma viagem tiverem itens diferentes, fica a que tiver mais itens. Com a mesma quantidade, vale a mais recente.
- O serviço atende uma única FC e identifica cada submarino só pelo slot, então o ID da casa não precisa ser igual nos PCs.
- Leitura sem itens nunca apaga uma viagem.
- Itens são comparados por ID, porque o nome muda conforme o idioma do jogo.
- No status de um submarino, vale a leitura feita por último, usando o horário do servidor em que ela chegou.
- A edição da mensagem é sempre gerada a partir do estado salvo, nunca de um envio isolado.
- Se o Discord falhar ou limitar as edições, o estado continua salvo e a próxima edição corrige o painel.

## Discord: o /fcc
- O app registra um comando só, `/fcc`, com o mesmo nome em qualquer idioma. Ele abre um menu que só quem chamou vê, e cada botão troca o conteúdo da mesma mensagem.
- Seções para todos: Painel, Subs, Loot (hoje, 7 ou 30 dias), Estoque e Lucro. "Postar no canal" manda a seção aberta para todo mundo ver, dizendo quem postou e sem marcar ninguém.
- Ajustes, só para quem tem "Gerenciar servidor": reenviar o painel, agrupar avisos, ligar ou desligar o resumo semanal, baixar o log do plugin e o idioma do menu. A permissão é conferida a cada clique, não só ao abrir o menu.
- O idioma do menu é da FC (padrão português para a FC atual). Os textos das seções ainda vêm em português até a 0.14.
- Os comandos antigos (`/subs`, `/loot`, `/estoque`, `/lucro`, `/painel`, `/avisos`, `/resumo`, `/log`) somem ao registrar o `/fcc`. Enquanto algum ainda aparecer no Discord, ele abre a seção equivalente do `/fcc`.
- Servidor que não é o da FC recebe "ainda não está ligado a uma FC". Na 0.13 essa ligação vem do registro de FCs.
