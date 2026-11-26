# Novidades

As mudanças do FC Companion, em português. É a mesma lista do `CHANGELOG.md`, versão por versão,
e é o que a janela de novidades mostra quando o plugin está em português. Regras de cada item:

- Uma linha, `- **Rótulo:** texto curto`, com umas 12 palavras no máximo.
- Seções sempre nesta ordem: Added, Changed, Fixed, Removed (os títulos ficam em inglês, a janela traduz).
- Os mesmos termos do plugin: submarino, airship, campo, rega, estoque, materiais de reparo, casa da FC.
- Update grande ganha `<!-- big -->` depois da versão, e os itens dele viram cards.

## [0.10.5] - 2026-11-26

### Fixed
- **Loot:** viagens salvas com data antiga passam para o dia certo.

## [0.10.4] - 2026-11-26

### Fixed
- **Loot:** viagens coletadas sem o plugin contam no dia certo.

## [0.10.3] - 2026-11-26

### Fixed
- **Loot:** salvo pelo relatório da última viagem de cada submarino, mesmo coletado sem o plugin.

## [0.10.2] - 2026-11-25

### Added
- **Linha do tempo:** as próximas 24 horas, 3 dias ou 7 dias numa barra só.
- **Cards de submarino:** rank, volta, progresso da viagem, peças e desgaste.
- **Ícones dos itens:** ícones do jogo no loot e o melhor item do dia.
- **Visual novo:** cores do FC Companion; o estilo do Dalamud não muda.
- **Botão Novidades:** nas configurações, para abrir esta lista de novo.
- **Botão do Ko-fi:** na janela, nas configurações e aqui.
- **Código da FC:** ligue o plugin à FC com um código curto.
- **Conta do Discord:** ligue com um código de 6 números, para eventos.
- **Pedidos de entrada:** o código da FC vira um pedido; o dono ou um admin aceita.
- **Aba FC:** membros e pedidos para todos; dono e admins aceitam e expulsam.

### Changed
- **Um comando só:** `/fcc` no jogo e no Discord.
- **Menu do Discord:** botões no lugar dos comandos antigos em português.
- **Novidades:** lista curta por tipo; cards só em update grande.
- **Estoque:** uma informação por linha.
- **Casa da FC:** dias restantes numa barra vermelha.
- **Desempenho:** menos trabalho por frame com muito histórico de loot.

### Fixed
- **Alts de outra FC:** não enviam mais dados para o painel da sua FC.
- **Privacidade:** o log não guarda mais nada do chat.

## [0.10.0] - 2026-11-23

### Added
- **Airships:** volta, rank, loot e aviso próprio.
- **Casa da FC:** dias desde a última visita, avisos aos 30, 40 e 44.
- **Previsão do estoque:** a data em que tanques e materiais acabam.
- **Lucro:** as últimas 4 semanas e o gil de cada rota.
- **Resumo semanal:** um post no Discord toda segunda de manhã.

## [0.9.0] - 2026-11-22

### Added
- **Comandos no Discord:** painel, avisos, loot, estoque, submarinos e log.
- **Campos:** cadastre um canteiro ou vaso selecionando ele.

### Changed
- **Nome:** o FC Timers agora se chama FC Companion.
- **Visão geral:** uma aba nova, com um card por assunto.
- **Cores:** verde para pronto, amarelo para em breve, vermelho para urgente.
- **Configurações:** janela própria, aberta pela engrenagem.
- **Boas-vindas:** passos para o primeiro uso.
- **Avisos:** notificação do Dalamud, linha no chat e som por tipo.
- **Horários:** passe o mouse num tempo para ver o horário exato.
- **Log:** só personagens ativos enviam.

## [0.8.6] - 2026-11-22

### Changed
- **Materiais de reparo:** contados por reparo de todos os submarinos.
- **Aviso de reparo:** uma mensagem no Discord para todos os submarinos.

## [0.8.5] - 2026-11-03

### Fixed
- **Nomes dos campos:** sem o ícone do jogo sobrando no Discord.
- **Nomes automáticos:** usam o número do canteiro que o jogo mostra.

## [0.8.4] - 2026-11-03

### Changed
- **Log:** guarda as mensagens do jogo depois dos menus da casa.

## [0.8.3] - 2026-11-03

### Fixed
- **Campos:** sem repetição entre idiomas diferentes do jogo.
- **Canteiros de fora:** ganham o número que aparece no menu.
- **Entrada Plantação:** some quando um campo com nome é regado.

## [0.8.2] - 2026-11-03

### Changed
- **Log:** enviado logo depois de ações importantes e ao fechar o jogo.

## [0.8.1] - 2026-11-03

### Changed
- **Log:** enviado a cada 30 minutos quando tem algo novo.
- **Avisos de submarino:** avisos próximos viram uma mensagem só.

## [0.8.0] - 2026-11-02

### Added
- **Estoque:** tanques e materiais de reparo, com rodadas e reparos restantes.
- **Detalhes do submarino:** rank, build, rota e desgaste das peças.
- **Preços do market:** itens contam pelo preço do Universalis quando é maior.
- **Avisos no Discord:** reparo, estoque baixo e setor novo.
- **Lembretes:** submarino parado e vaso de dentro sem rega.
- **Inglês:** escolha de idioma nas configurações.

### Changed
- **Janela principal:** dividida em abas.

## [0.7.4] - 2026-11-02

### Changed
- **Novidades:** separadas por tipo de mudança.

## [0.7.3] - 2026-11-02

### Changed
- **Painel do Discord:** uma linha por campo.

### Fixed
- **Rega:** vai para o campo certo mesmo depois de tirar o alvo.

## [0.7.2] - 2026-11-02

### Changed
- **Tempos de rega:** 24 horas para regar, 48 horas para murchar.

### Removed
- **Ajustes de rega:** saíram da janela principal.

## [0.7.1] - 2026-11-02

### Added
- **Enviar log:** manda o log de diagnóstico para o serviço da FC.
- **Log automático:** enviado quando o loot de um submarino se perde.

### Changed
- **Plantação:** "Plantas" agora se chama "Plantação".

## [0.7.0] - 2026-11-02

### Added
- **Sincronia nos dois sentidos:** veja o que os outros membros leram.
- **Rega por campo:** detectada sozinha ou marcada com um botão.
- **Avisos no Discord:** submarinos e campos, com lembretes.
- **Log de diagnóstico:** botão de copiar e modo detalhado.
- **Novidades:** esta lista, dentro do jogo.

### Changed
- **Captura do loot:** na hora em que chega o resultado da viagem.
- **Dias do loot:** o mesmo dia em todos os PCs.
- **Painel do Discord:** totais do dia sem a lista de itens.

## [0.6.1]

### Fixed
- **Lista de submarinos:** sem o quinto item repetido.
