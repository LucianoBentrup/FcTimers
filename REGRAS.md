# Regras do painel e da sincronia

## Painel
- Existe uma única mensagem no Discord, sempre editada.
- Ela mostra o status atual, a plantação e o total do loot do dia atual.
- A lista de itens do dia fica fora do painel, para a mensagem ficar leve. O código continua no serviço (`SHOW_ITEMS`) para um bot no futuro.

## Avisos
- Todo aviso marca o cargo @Subs e é apagado 8 horas depois de enviado.
- 1 hora antes de um submarino voltar: "Lembrete: Em 1 hora os submarinos estarão prontos para coleta", com os nomes. Quem volta até 15 minutos depois entra no mesmo lembrete.
- Se faltar menos de 30 minutos quando o serviço puder enviar o lembrete, ele não sai, e só sai o aviso de pronto.
- Quando um submarino volta, sai o aviso de pronto. Submarinos que voltam juntos entram no mesmo aviso.
- Plantação, por plot: lembrete 1 hora antes de poder regar, aviso quando pode regar, e um aviso 3, 2 e 1 hora antes de morrer.
- Cada viagem e cada rega avisa uma vez só. Algo visto pela primeira vez quando já estava pronto não gera aviso.
- Quando um buff da mesa da roda fica pronto, sai um aviso marcando @Subs, uma vez só. Buff visto pela primeira vez já pronto não gera aviso.
- Lembrete de esquecido: um sub que voltou há mais de 2 horas e não saiu de novo ganha um lembrete, uma vez por viagem. Um vaso de dentro de casa ganha um lembrete quando outro vaso da mesma casa foi regado há 15 minutos ou mais e ele, que já podia ser regado, não foi.
- Nos avisos de plantação, só os vasos de dentro de casa ou de quarto privado aparecem pelo nome. Os canteiros de fora são regados juntos, então aparecem como "a plantação" ou "os canteiros de fora".
- O aviso de estoque baixo diz quantas viagens e rodadas os tanques ainda aguentam.
- Reparo: quando a peça mais gasta de um sub fica abaixo de 30%, sai um aviso com os subs gastos e os kits que o reparo completo gasta. Ele volta a avisar depois de um reparo.

## Loot do dia
- Só valem as posições de 0 a 3. A 5ª posição da lista do jogo repete um dos 4 submarinos e é ignorada no plugin e no serviço.
- Cada viagem é identificada pelo slot do submarino e pelo horário de retorno.
- O total do dia é sempre recalculado somando todas as viagens daquele dia. Nada zera entre coletas.
- Reler ou reenviar a mesma viagem nunca soma de novo.
- A viagem pertence ao dia em que o submarino voltou, no fuso de Brasília, no painel e na janela do plugin.
- À meia-noite o painel passa a mostrar o novo dia. Os dias anteriores ficam salvos e nunca são apagados.

## Buffs ativos
- Os status do personagem dizem quais buffs estão ativos, e o valor do status indica o nível.
- O tempo só é carregado pelo jogo quando alguém abre a aba de ações. Depois de lido uma vez, o horário de término fica guardado no plugin e no serviço.
- Se o buff surgir com o plugin ligado há pelo menos 1 minuto, o término é estimado em 24 horas. A aba de ações, quando aberta, traz o valor exato.
- Sem horário conhecido, o painel mostra `?` e o horário da leitura. O nível ausente fica só com o nome-base.

## Plantação
- Cada plot ou vaso tem o seu horário. Ele é identificado pela casa (território, ward e plot), pelo quarto privado quando houver e pela posição do objeto, que só muda se alguém mover o plot.
- O plugin registra a rega quando o jogo mostra "This crop is doing well." (mensagem 4017) ou ao plantar (4015), no plot que estiver selecionado. Sem plot selecionado, a rega vai para o plot geral "Plantação". Ainda falta confirmar no jogo que essas mensagens só aparecem nesses momentos.
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
