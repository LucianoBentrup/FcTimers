# Regras do painel e da sincronia

## Painel
- Existe uma única mensagem no Discord, sempre editada.
- Ela mostra o status atual e o loot do dia atual.

## Avisos
- Quando um submarino volta, o serviço envia uma mensagem curta marcando o cargo @Subs.
- O aviso é apagado na volta do submarino mais demorado daquele momento. Sem nenhum em viagem, é apagado depois de 24 horas.
- Submarinos que voltam juntos entram no mesmo aviso, e cada viagem avisa uma vez só.
- Uma viagem vista pela primeira vez quando já tinha voltado não gera aviso.

## Loot do dia
- Cada viagem é identificada pelo slot do submarino e pelo horário de retorno.
- O total do dia é sempre recalculado somando todas as viagens daquele dia. Nada zera entre coletas.
- Reler ou reenviar a mesma viagem nunca soma de novo.
- A viagem pertence ao dia em que o submarino voltou, no fuso de Brasília.
- À meia-noite o painel passa a mostrar o novo dia. Os dias anteriores ficam salvos e nunca são apagados.

## Buffs ativos
- Os status do personagem dizem quais buffs estão ativos, e o valor do status indica o nível.
- O tempo só é carregado pelo jogo quando alguém abre a aba de ações. Depois de lido uma vez, o horário de término fica guardado no plugin e no serviço.
- Se o buff surgir com o plugin ligado há pelo menos 1 minuto, o término é estimado em 24 horas. A aba de ações, quando aberta, traz o valor exato.
- Sem horário conhecido, o painel mostra `?` e o horário da leitura. O nível ausente fica só com o nome-base.

## Personagens
- Cada personagem entra na lista do plugin na primeira vez que loga, desmarcado.
- Só um personagem marcado lê dados do jogo e os envia ao serviço.
- O plugin ignora leituras em duty ou trocando de área, e só aceita uma lista que ficou igual por 5 segundos.
- Um buff com tempo sai sozinho do painel quando acaba. Um buff ativado com todos offline só aparece na próxima leitura.

## Sincronia entre membros
- O plugin só envia o que acabou de ler do jogo, nunca dados antigos salvos no PC.
- Toda alteração no serviço passa por um único ponto, uma de cada vez, para um envio não sobrescrever o outro.
- A mesma viagem enviada por 2 pessoas vira um único registro.
- Se 2 leituras da mesma viagem tiverem itens diferentes, fica a que tiver mais itens. Com a mesma quantidade, vale a mais recente.
- O serviço atende uma única FC e identifica cada submarino só pelo slot, então o ID da casa não precisa ser igual nos PCs.
- Leitura sem itens nunca apaga uma viagem.
- Itens são comparados por ID, porque o nome muda conforme o idioma do jogo.
- No status de um submarino, vale a leitura feita por último, usando o horário do servidor em que ela chegou.
- A edição da mensagem é sempre gerada a partir do estado salvo, nunca de um envio isolado.
- Se o Discord falhar ou limitar as edições, o estado continua salvo e a próxima edição corrige o painel.
