# Plano da 0.7.0

Pesquisa feita em 02/11 na biblioteca do jogo usada pelo Dalamud (FFXIVClientStructs 7.57) e no código do Submarine Tracker.
Nada daqui foi testado no jogo ainda. Cada item diz o que está confirmado e o que falta confirmar.

## 1. Loot dos subs, rápido o bastante para o AutoRetainer

**Causa provável do Tatl:** hoje o plugin verifica os subs uma vez por segundo, e o loot só existe na memória entre a chegada do resultado e o sub sair de novo. Com o AutoRetainer, esse intervalo pode durar menos de um segundo.

**O que a pesquisa mostrou:**
- A 5ª posição da lista dos subs, que a 0.6.1 passou a ignorar, é o sub que está sendo coletado. O Submarine Tracker lê o loot exatamente dela.
- O Submarine Tracker não verifica em intervalos: ele escuta o aviso de "resultado da viagem" que o servidor manda e lê o loot no mesmo instante. Esse aviso tem o código 721343.
- A tela de resultado tem os itens prontos no próprio jogo (`AgentSubmersibleExplorationResult` → `Data` → até 10 pares de item e quantidade).

**Como implementar, em camadas:**
1. **Evento da tela de resultado:** usar o `IAddonLifecycle` do Dalamud, que é API oficial, para ler o loot quando a tela de resultado abre. Ele não depende de assinatura de memória, então não quebra com patch.
2. **Aviso do servidor:** escutar o mesmo aviso que o Submarine Tracker usa, como reforço quando a tela não abre. Ele só observa e chama a função original antes, sem mudar nada no jogo. A assinatura pode quebrar em patch, então, se falhar, o plugin desliga só esse reforço e avisa no log.
3. **Leitura atual:** mantém a leitura dos 4 subs, só que dentro da oficina e a cada frame em vez de a cada segundo, como última rede de segurança.

As três camadas usam a mesma chave de viagem (posição + horário de volta), então ler a mesma viagem duas vezes não soma em dobro.

**Falta confirmar no jogo:** se o AutoRetainer abre a tela de resultado ou pula direto, e se a 5ª posição continua apontando para o sub certo durante a coleta.

## 2. Log de diagnóstico

- O que registrar: entrou ou saiu da oficina, chegada do resultado, sub voltou com ou sem itens, viagem salva, sub saiu de novo, loot perdido (sub saiu sem viagem salva) e envios ao serviço.
- Cada linha tem data, hora e fuso.
- Fica em `diagnostico.txt`, na pasta de configuração do plugin, com no máximo 500 linhas.
- Também vai para o `dalamud.log`.
- Botão **Copiar log** no `/fct`: copia o texto, com versão e fuso no topo e nunca a chave da FC, para colar no Discord.
- Flags na configuração: **Básico**, só eventos de loot e erros, ligado por padrão. **Detalhado**, que inclui cada leitura dos subs e os envios.

## 3. Sincronia nos dois sentidos

- **Serviço:** rota nova `GET /state`, com a mesma chave, que devolve o estado (subs, mesa, buffs ativos), as viagens dos últimos 7 dias e o horário do servidor.
- **Plugin:** busca a cada 1 minuto com o `/fct` aberto, e a cada 5 minutos com ele fechado. Dois PCs o dia inteiro dão menos de 4 mil pedidos por dia.
- **Mistura dos dados:** vale a leitura mais nova pelo `observedAt`, a mesma regra do serviço. O loot é unido por chave de viagem.
- **Uso dos dados:** a janela, a barra do jogo e os avisos no chat usam os dados misturados.
- **Avisos no chat:** cada viagem avisa uma vez por PC.
- **Situação na janela:** mostra "Sincronizado às 14:02" ou "Sem conexão, mostrando dados deste PC".

## 4. Mesmo fuso em tudo

- A janela passa a agrupar os dias pelo fuso de Brasília (`America/Sao_Paulo`), igual ao painel.

## 5. Jardim (plots)

**O que a pesquisa mostrou:**
- O jogo não tem uma estrutura com o estado das plantas na memória. O que existe é a tela de escolher semente e terra (`AgentHousingPlant`).
- O único rastreador que encontrei, o Gardening Tracker da lotlab, é para ACT e só para o servidor chinês. Ele grava cada ação (plantar, cuidar, adubar, colher) a partir do tráfego de rede e calcula o resto.

**Como implementar:**
- Registrar as ações: quando alguém planta, cuida ou colhe, o plugin anota o plot, a semente e o horário.
- Calcular o resto a partir disso: próximo cuidado e previsão de colheita, usando o tempo de crescimento de cada semente.
- Ler a ação pela mensagem do chat ou pela tela do plot, sem automatizar nada.
- Plot que ninguém tocou com o plugin ligado fica como "desconhecido".

**Falta confirmar:**
- Prazo do cuidado (o que se costuma dizer é 24 horas para murchar e 48 horas para morrer).
- Tempo de crescimento de cada semente, que não está nas planilhas do jogo e teria que vir de tabela da comunidade.
- Quais mensagens o jogo mostra em português e inglês para cada ação.
- Se dá para identificar o plot (casa e posição).

É o item mais incerto. Vale fazer depois dos outros.

## Ordem sugerida
1. Log de diagnóstico, para medir o resto.
2. Loot rápido.
3. Mesmo fuso.
4. Sincronia.
5. Jardim, depois de pesquisar no jogo.

Fontes: código do Submarine Tracker (`Manager/HookManager.cs`), em https://github.com/sjelatex/SubmarineTracker, e o Gardening Tracker da lotlab, em https://github.com/lotlab/ffxiv-gardening-tracker.
