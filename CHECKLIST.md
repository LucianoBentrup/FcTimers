# Checklist da 0.7.0

[x] = feito no código e testado fora do jogo. Falta testar no jogo.

## Diagnóstico
- [x] Log do que o plugin vê em cada sub: entrou ou saiu da oficina, voltou com ou sem itens na memória, viagem salva, saiu de novo, loot perdido e envio ao serviço.
- [x] Botão no `/fct` que copia o log, já sem a chave da FC, para colar no Discord.
- [x] Opções (flags) para ligar ou desligar níveis de detalhe do log.

## Loot
- [ ] Descobrir por que o loot do Tatl não entrou em 02/11, e os cerca de 225 mil gil que faltaram em 31/10.
- [x] Ler o loot pela tela de resultado da viagem, sem depender do intervalo entre voltar e sair de novo.
- [x] Pegar o loot rápido o bastante para o AutoRetainer, que coleta e reenvia em menos de 1 segundo.
- [x] A janela do plugin agrupa os dias pelo fuso do PC, e o painel pelo de Brasília. Usar o mesmo nos dois.

## Sincronia
- [x] O plugin também recebe dados do serviço, e não só envia: timers dos subs, buffs da mesa, buffs ativos e loot de todos os dias, incluindo o que o outro membro leu.
- [x] Os dados recebidos aparecem na janela do `/fct`, na barra do jogo e nos avisos no chat, então os dois PCs mostram a mesma coisa.
- [x] Uma leitura feita no próprio PC vale mais que um dado recebido mais antigo, seguindo a mesma regra do serviço: vale a leitura feita por último.
- [x] Sem chave ou com o serviço fora do ar, o plugin continua usando só os dados locais e mostra quando foi a última sincronia.
- [x] Buscar com a janela aberta, e em intervalos maiores com ela fechada, para não pesar no jogo nem no limite da Cloudflare.

## Jardim (plots)
- [x] Prioridade: lembrar quando dá para dar água de novo. Hoje o amigo planta onion.
- [x] Capturar o momento em que alguém dá água e calcular o próximo horário a partir dele, com uma tabela de intervalos.
- [ ] Teste no jogo: dar água com o log ligado e ver o que o jogo mostra (mensagem no chat, texto na tela do plot).
- [ ] Mostrar o que está plantado em cada plot e quando fica pronto para colher. Ficou para depois: a 0.7.0 só cuida da rega.
- [x] Avisar quando precisa dar água, antes de passar do prazo.
- [x] Levar os plots para o painel do Discord e para a sincronia entre os plugins.
- [ ] Pesquisar de onde o jogo tira esses dados e se aparecem sem abrir o plot. Ler só o que o jogo já mostra, sem automatizar a rega nem a colheita.

## Avisos no Discord
- [x] Mostrar as plantas no painel do Discord, com o horário de dar água de novo.
- [x] Lembrete 1 hora antes, marcando @Subs: "Lembrete: Em 1 hora os submarinos estarão prontos para coleta."
- [x] Lembrete 1 hora antes, marcando @Subs: "Lembrete: Em 1 hora as plantas estarão disponíveis para regar."
- [x] Aviso na hora em que fica pronto, marcando @Subs, para subs e para plantas.
- [x] Todo aviso, de 1 hora antes ou de pronto, se apaga 8 horas depois de enviado. Isso substitui a regra atual, que apaga na volta do sub mais demorado.
- [x] Subs ou plantas que ficam prontos juntos entram no mesmo aviso, e cada um avisa uma vez só.

## Pedidos durante o trabalho
- [x] Tirar "Itens de hoje" do painel, mantendo o código para um bot no futuro.
- [x] Painel: "Pronta para regar. Morre <tempo>" e aviso 3, 2 e 1 hora antes de a planta morrer.
- [x] Rega e avisos por plot, no plugin e no Discord.
- [x] Changelog no jogo, em inglês, que abre depois de cada atualização (texto para revisar).

## Antes de publicar
- [ ] Testar uma coleta completa dos 4 subs e comparar com o relatório do jogo e com o baú.
- [ ] Trocar a chave da FC, que tem só 6 caracteres.
