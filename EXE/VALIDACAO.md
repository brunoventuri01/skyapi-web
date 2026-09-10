# Validação — SkyAPI 1.1.5

Data: 09/09/2026. Acrescenta a validação da 1.1 ao registro da 1.0.4, mantido no fim do documento.

## Ajustes da 1.1.7 — 09/09/2026

Rodada a partir do uso real do aplicativo pelo solicitante. Sete pontos relatados, sete tratados.

**Uso aparecia com dois sinais de porcentagem.** `Display.Percent` já devolve o `%` e a tela somava outro:
`1,49 GB · 3%%`. A montagem da coluna saiu da tela e virou `Display.Usage`, no núcleo, com teste — o erro só
era possível porque a composição estava solta na interface.

**A lista de produtos de destino não abria.** O `ComboBox` nascia com `IsEnabled=false`, porque ainda não havia
catálogo, e nada o reabilitava depois da consulta: a mensagem dizia "5 produtos retornados pela API" e a lista
continuava travada. Agora consultar o domínio habilita a lista quando ela vem preenchida, trocar o domínio
volta a desabilitar, e o próprio aviso pede para escolher o produto acima.

**Desconectar no meio de uma substituição travava a barra.** A chamada seguinte morria em "Configure a conexão
antes de executar.", a operação encerrava e a barra ficava parada na metade, como se algo ainda andasse. Duas
correções: qualquer erro devolve a barra a zero, e a substituição interrompida passa a registrar uma linha
**Pendente** dizendo que a renomeação pode já ter sido enviada e quais endereços conferir antes de repetir.
Antes, a interrupção não deixava registro nenhum.

**Sete dias valem para os dois lados da busca.** A regra só olhava o lado que vai para a API — remetente nas
enviadas, destinatário nas recebidas. Buscar "de `gmail.com` para as minhas contas" em 30 dias passava. Agora
qualquer lado preenchido por domínio encurta a janela: os períodos maiores ficam desabilitados, o calendário
não recua além de 7 dias e a execução recusa. O limite também entrou no núcleo (`ValidateSearch`), aplicado a
toda chamada de relatório, e não só na tela. O menu de período passou a reagir ao que se digita, não só à troca
de opção entre Domínio e Contas.

**Paginação não reprova mais o relatório.** O aplicativo pedia páginas de 50 e, ao receber a última vazia,
comparava com `total_count`: se o total estivesse à frente, lançava "Relatório incompleto: a API encerrou a
paginação antes do total" e a busca inteira virava falha. O total anunciado, porém, anda enquanto a consulta
roda — mensagem nova entrando no período — e isso reproduz o sintoma relatado: janelas terminando *hoje* davam
parcial, a mesma janela terminando ontem dava sucesso. Agora o total serve só para continuar paginando:

- página vazia com total à frente: uma nova tentativa do mesmo ponto antes de encerrar;
- página repetida: encerra, sem laço e sem exceção;
- o que veio permanece na tela e no CSV, e a linha de resultado informa "a API entregou X dos Y registros que
  anunciou" quando houver diferença.

**O corte da API tem tratamento: dividir o período.** O uso real mostrou o número: **350 registros entregues de
1003 anunciados**, sempre em página cheia seguida de página vazia — assinatura de corte por volume, não de
total desatualizado. O relatório passou a ser lido por fatias de período, a mesma saída que o relatório de
login já usava: quando uma fatia é cortada, ela é dividida ao meio e cada metade é consultada de novo, até
fechar inteira ou chegar ao piso de 60 segundos. Detalhes que evitam relatório errado:

- os registros de uma fatia **só entram no resultado quando a fatia fecha**; do contrário a parte já lida
  entraria duas vezes na reconsulta;
- as metades são `[início, meio]` e `[meio+1s, fim]`, sem sobreposição — mesma divisão do login;
- **página curta seguida de vazia não divide nada**: aí os dados acabaram mesmo e o total anunciado é que
  estava adiantado;
- **página repetida não divide**: significa que a API ignorou o `offset`, e fatiar só multiplicaria chamadas
  repetindo o mesmo registro. Encerra e avisa.

Com a divisão, o resultado sai na ordem das fatias (mais antigas primeiro), e não mais na ordem única da API.

**Sugestão de upgrade deixou de depender do cadastro do cliente.** O catálogo de `domain/{dominio}/mail-products`
traz **só o que aquele cliente contratou no painel**. Uma caixa em 70,7% de 50 GB, num cliente sem nenhum
produto de 100 GB, recebia "Nenhum upgrade SkyMail compatível disponível" — o upgrade existe, o que falta é o
produto no painel. A análise passou a procurar primeiro no catálogo do domínio, que é o que dá para enviar
hoje, e depois na linha SkyMail publicada (5, 25, 50, 100, 200 e 500 GB). No segundo caso a linha diz o produto
e avisa que ele não está contratado no painel do cliente, com o link de como adicionar na própria tela. O envio
para a alteração em lote recusa essas contas com a explicação, em vez de deixar o lote parar na conferência.

Não foi possível medir contra a API real nesta rodada: as credenciais foram enviadas no chat e não são usadas
por aqui. O roteiro está em `ROTEIRO-DE-TESTE.md`.

**Testes.** 168 verificações no núcleo, 168 passando, nenhuma chamada real à Skymail — nove novas cobrem o
corte da API com divisão de período (120 registros num serviço que corta em 100: sai inteiro e sem repetir), o
total adiantado com página curta, a página repetida, a janela de 7 dias com domínio no lado peneirado, os 30
dias com contas dos dois lados e o percentual com um único sinal.

**Limitação conhecida, herdada da 1.1.6.** `tests/SkyAPI.UiTests` continua bloqueado pelo Controle de
Aplicativos do Windows ao carregar o assembly (0x800711C7). A validação visual segue pendente e nenhuma
política do Windows foi alterada.

## Correção dos exemplos de preenchimento — 1.1.6

- Removido o padding duplicado no template dos TextBox; o exemplo acompanha o padding do editor e sua margem interna de 2 DIPs. A indicação permanece com foco, some quando há texto e reaparece ao apagar.
- Versões do aplicativo, projeto e assembly atualizadas para 1.1.6.
- O novo teste `tests/SkyAPI.UiTests` mede o cursor real com `GetRectFromCharacterIndex(0)`, em sete telas, nos dois temas e nas cinco escalas. Na 1.1.5, reproduziu 480 falhas de alinhamento em 1.440 verificações: desvio horizontal de 14 DIPs e vertical de 9 DIPs nos campos de várias linhas.
- A execução desse teste na 1.1.6 foi bloqueada pelo Controle de Aplicativos do Windows ao carregar SkyAPI.dll (0x800711C7). Portanto, a validação visual da versão corrigida permanece pendente. Nenhuma política do Windows foi alterada.
- Publicação Release self-contained win-x64 concluída com sucesso. Os 159 testes do núcleo passaram, sem chamadas reais à Skymail.
- O harness anterior também foi corrigido para comparar o exemplo com o cursor, em vez da borda externa do ScrollViewer.

O registro abaixo corresponde às versões anteriores; não representa uma validação visual da 1.1.6.

## Resultado da 1.1

- Núcleo e interface WPF compilados em Release pelo SDK .NET 8.0.424 no Windows 11: **zero erros, zero avisos**.
- **159 verificações automatizadas: 159 passaram, zero falhas.** Saída completa em `validation/test-results-v1.1.txt`.
- Nenhuma verificação automatizada chama a Skymail: o transporte é exercitado por um substituto em memória.
  As chamadas reais estão descritas na seção de integração abaixo.
- Rotas fora do escopo da 1.1 são recusadas antes de sair do aplicativo, com teste de regressão.
- Executável de homologação gerado em `release/v1.1-homologacao/SkyAPI.exe`. Os cartões dos módulos novos na
  tela inicial e o menu lateral foram conferidos na árvore de acessibilidade da compilação aberta no Windows.

## Integração com a API real — 09/09/2026

Executada com credenciais de uma conta de teste e o domínio `brunoventuri.skydemo.com.br`, fornecidos pelo
solicitante, em ambiente declarado por ele como descartável e sem cobrança. Usou o próprio `SkyAPI.Core`:
mesmo login, mesmo transporte, mesmos parsers. As credenciais foram passadas por variável de ambiente e não
estão gravadas em nenhum arquivo do projeto. Houve escritas e contratações, autorizadas explicitamente.

### Confirmado funcionando

- **Login real**, com o JWT assinado localmente aceito nas chamadas seguintes.
- **Leitura de caixa**: `mailbox/{conta}` traz `mail`, `productname`, `mailBoxQuotaSize.mailquotasize`/`used`,
  `mailalternateaddress`, `groups` e `renameprocessing`, e `MailboxInfo` interpreta todos. Caixas Exchange podem
  vir sem `mailBoxQuotaSize`: quota e uso ficam desconhecidos, não zero.
- **Catálogo de produtos** do domínio e **saldo por cliente**.
- **Alteração de licença**: sem saldo a API recusa e a recusa é classificada como falta de licença; com
  confirmação de compra a alteração conclui e a pós-validação confirma o produto novo. Testado nos dois
  sentidos, incluindo volta ao produto original.
- **Sugestão de upgrade** a partir do catálogo real: caixa de 5 GB sugeriu SkyMail 25 GB.
- **Criação de grupos em lote**, com e sem saldo, incluindo a continuação após confirmação de compra.
- **Relatório de login** e **relatórios de mensagens** recebidas e enviadas, com registros reais.

### Divergências encontradas na API e corrigidas

1. **`report/login` nunca funcionou.** A resposta é `data:{total_results,messages:[…]}`, não um array em `data`,
   e cada registro traz `User`, `@timestamp`, `Motivo`, `_id` e um campo `message` com o registro repetido como
   JSON, onde ficam `RemoteIP`, `Protocolo`, `Tipo` e `Hostname`. O código lia `user`, `date`, `ip` e `motivo`
   em minúsculas, com busca sensível a maiúsculas, então **toda consulta terminava em "Resposta de login
   inválida."** e o IP não tinha como ser preenchido. Corrigido; a tela e o CSV ganharam a coluna **Protocolo**.
2. **`accounttype` da caixa não usa os nomes do catálogo.** O catálogo diz `SkyMail Premium 500GB` e
   `SkyExchange 50GB`; a caixa responde `accounttype` `SkyMail 500GB` e `Exchange 50GB`. Só o campo
   `productname` traz o nome do catálogo. Como o código lia `accounttype`, a regra que bloqueia Exchange
   (`StartsWith("SkyExchange")`) não disparava e dava a mensagem genérica em vez de orientar a abrir chamado; a
   verificação de "caixa já usa este produto" não reconhecia o produto; e a pós-validação de uma alteração
   falharia, marcando **Pendente** e interrompendo o lote. `MailboxInfo.Product` passou a vir de `productname`.
3. **O PUT quer o rótulo da caixa, não o nome do catálogo.** `accounttype=SkyMail Premium 500GB` responde **404**
   com saldo disponível; `accounttype=SkyMail 500GB` conclui; `accounttype=302` (productId) também dá 404. O
   aplicativo passou a **aprender o rótulo pela própria API**, lendo o `accounttype` de uma caixa que já usa o
   produto (`client/{id}/product/{id}` devolve `items` com essas caixas), em vez de adivinhar transformação de
   nome. A conferência mostra o nome que será enviado. Sem nenhuma caixa usando o produto, o aplicativo envia o
   nome do catálogo e avisa na conferência; um 404 nessa situação passou a explicar a divergência de nome.
4. **`confirm_purchase=true` contrata exatamente uma licença**, a consumida pela própria chamada. Medido: saldo
   de grupos 1, pedido de 4 grupos — o primeiro criou, os outros três foram recusados; a flag em um deles criou
   aquele grupo e **os demais continuaram recusados**; o saldo permanece 0 porque a compra e o consumo ocorrem
   juntos. A regra anterior de enviar a flag **uma única vez** por lote contratava 1 licença das N autorizadas.
   Agora a flag vai em cada grupo autorizado, a janela de confirmação diz "serão contratadas até N licenças, uma
   por grupo", e o resultado informa quantas confirmações foram enviadas. Reconferido: 4 grupos, 4 criados.
5. **Relatórios de mensagens: data e tamanho vinham vazios.** `Tamanho` não é campo de topo — existe só dentro
   do `message` embutido das recebidas, como número. As enviadas **não têm `@timestamp`**: usam `DataEnvio`,
   `DataEntrega` e `timestamp`, então a data de toda mensagem enviada ficava vazia. Corrigido; enviadas não têm
   tamanho na API e o campo fica vazio, sem invenção.

### Comportamentos da API que ficam registrados

- **O estoque é do cliente, não do domínio**, confirmado na prática: o cliente 40958 abrange
  `brunoventuri.skydemo.com.br`, `thiagosousa.skydemo.com.br` e `nexusecosystem.com.br`. A conferência de grupos
  soma o mesmo estoque uma única vez por cliente, e não por domínio.
- `amount` **não é sempre maior que** `count`: há produtos com `amount` 2 e `count` 15. Por isso o saldo é
  apresentado como `amount - count` limitado a zero, e a ausência dos campos é mostrada como desconhecida.
- O filtro por endereço nos relatórios só encontra caixas **existentes**. Remetentes de caixas já excluídas
  aparecem apenas na consulta por domínio. Não é falha do aplicativo.
- Os relatórios aceitam domínio no lugar do endereço, e uma consulta por domínio em janelas longas estoura o
  tempo limite de 40 segundos do cliente. O aplicativo consulta sempre por conta, então não usa esse caminho.

## QA visual — 09/09/2026

Feito com um harness fora do produto (`validation/qa-harness.cs`) que instancia a janela real, navega por cada
tela, aplica tema e escala, salva um PNG e vasculha a árvore visual em busca de defeitos. As 91 imagens estão em
`validation/qa-v1.1/`.

Cobertura: as onze telas — inicial, menus de Licenças e Relatórios, alteração de licenças, análise para upgrade,
criação de grupos, substituição de colaborador, mensagens recebidas, mensagens enviadas, relatório de login e
relatório de caixas — nos temas **claro e escuro**, nas escalas **90%, 100% e 140%**, e em **janela estreita**
(760 px) na escala padrão. Mais três telas em modo demonstração.

Verificações automáticas em cada combinação: nenhum texto de linha única cortado por falta de espaço, e nenhuma
rolagem horizontal necessária fora das tabelas, onde ela é natural. **Resultado: zero ocorrências nas 91.**

Conferência a olho de uma amostra (grupos claro 100%, upgrade escuro 140%, login escuro 100%, enviadas claro
140%, substituição escuro 100%, inicial e caixas em janela estreita): moldura, barra lateral, cartões e
formulários corretos nos dois temas; em janela estreita os cartões passam a uma coluna e os botões do topo vão
para a própria linha; em 140% a lista lateral rola verticalmente, como previsto; a tabela de caixas, com nove
colunas, rola na horizontal dentro da própria grade.

Um defeito real encontrado e **corrigido**: os seletores de data vinham com a moldura padrão do Windows — fundo
branco e cantos retos — destoando de todos os outros campos, e no tema escuro ficavam brancos com texto escuro.
Passaram a usar a paleta do aplicativo, com o campo interno transparente dentro da moldura do tema.

Também conferido nesta rodada: em modo demonstração, os módulos novos mostram o aviso **"Não é possível
demonstrar este módulo"** em vez do aviso genérico que prometia simulação, e o teste do harness reprova qualquer
tela nova que volte a prometer simulação.

Não coberto: as telas com resultados preenchidos, a janela de confirmação e a exportação, que dependem de uma
sessão autenticada pela interface. Uso com mouse e teclado por uma pessoa e monitores com DPI diferente também
continuam fora.

## Mudanças da 1.1.1 — 09/09/2026

Revisão de usabilidade pedida pelo solicitante, com quatro achados de código:

- **ComboBox editável não mostrava nem aceitava texto.** O template do aplicativo não tinha
  `PART_EditableTextBox`, exigido pelo WPF: o campo "Uso mínimo (%)" aparecia vazio e não dava para digitar.
  Corrigido no template, com o campo aparecendo apenas quando a lista é editável.
- **Análise para Upgrade dependia de planilha.** Agora, com a lista de contas vazia, o aplicativo descobre as
  caixas do domínio pela própria API: cruza os produtos de e-mail do domínio com os `items` de cada produto do
  cliente. Conferido ao vivo em `brunoventuri.skydemo.com.br`: **15 caixas**, igual ao `totalmailbox` do
  domínio, sem grupos nem itens de outros domínios. A primeira versão da busca trazia grupos junto e gerava
  linhas "falhou"; o filtro por produtos de e-mail resolveu.
- **Gestão de membros de grupos existe na API.** Medido: `PUT group/{mail}` com `rfc822member[endereco]=true`
  inclui e valor vazio remove **apenas aquele endereço**, sem tocar nos demais — mesma sintaxe dos apelidos.
  Virou o módulo "Gerenciar membros de grupos", uma linha por alteração, agrupadas em um PUT por grupo.
- **Criação de grupos aceita uma pessoa por linha.** Linhas repetidas com o mesmo e-mail de grupo passam a
  somar pessoas, em vez de virarem "grupo duplicado", para não depender do separador `|`.

Também nesta versão: botão para exibir e copiar a senha nova na substituição de colaborador; texto de
orientação em cada tela; exemplo dentro dos próprios campos; botão **Baixar CSV de exemplo** por tela; a lista
de produtos de destino agora avisa que só traz o que está contratado no painel, com link para a documentação;
e a retirada dos relatórios de login e de caixas, a pedido do solicitante, por já existirem no painel Skymail.

O relatório de login continua implementado e testado no núcleo, sem tela: foi só a interface que saiu.

## Mudanças da 1.1.2 — 09/09/2026

**Operações deixaram de travar o aplicativo.** O estado de cada lote passou a viver na janela, um por módulo,
com resultados, status e pedido de parada próprios. Um lote continua enquanto a pessoa navega; só o módulo dele
fica bloqueado. A tela **Processos** lista o que está em andamento, desde quando, quantos registros saíram, e
permite parar ou abrir cada operação; o menu mostra a contagem ao lado do nome.

**Busca cruzada de mensagens.** Cada lado da consulta escolhe entre um domínio ou uma lista de contas. Medições
que definiram o desenho:

```text
sent  fromEmail=dominio ................................ 1 registro
sent  fromEmail=dominio + toEmail=endereço exato ....... 1 registro
sent  fromEmail=endereço + toEmail=dominio ............. 0  (silenciosamente vazio)
recv  toEmail=dominio + fromEmail=endereço exato ....... 1 registro
recv  toEmail=endereço + fromEmail=dominio ............. 0  (silenciosamente vazio)
```

Ou seja, **domínio só funciona no parâmetro principal**; no secundário a API devolve zero sem erro. Por isso o
aplicativo envia à API apenas o lado que é seu e aplica todo o resto localmente, o que também reduz a consulta
a uma chamada por valor do lado próprio. Conferido ao vivo, com resultado correto nos quatro casos: domínio →
domínio, domínio → contas, contas → domínio e sem filtro do outro lado.

Buscas em que o lado principal é um domínio ficam limitadas a **7 dias**; com contas, seguem os 90 dias. O
tempo limite das chamadas subiu de 40 para **60 segundos**.

## Mudancas da 1.1.3 - revisao de uso (09/09/2026)

Rodada a partir do uso real do aplicativo pelo solicitante.

**Erros deixaram de passar despercebidos.** Antes a mensagem saia pequena e cinza no meio da tela - houve caso
de o erro estar na tela e nao ser visto. Agora aparece uma faixa vermelha no topo, com icone, dizendo o que
esta errado, e o campo culpado ganha borda de erro e recebe o foco. As mensagens passaram a nomear o campo e a
dizer o que fazer, inclusive as de CSV sem cabecalho e de dominio digitado onde se esperava conta.

**Licencas de grupo contratam na primeira passada.** Antes a execucao falhava tudo, pedia confirmacao e mandava
executar de novo. Agora a conferencia mostra "FALTAM N LICENCAS" e, ao executar, cada grupo sem saldo ja vai com
a confirmacao de compra. A autorizacao continua explicita e unica, so que no lugar certo.

**Encoding investigado.** O assunto com acento chegava quebrado. Medicao: a resposta vem em ASCII puro com
escapes unicode, `Content-Type: application/json` sem charset, e a leitura do aplicativo bate byte a byte com o
que chega. Ou seja, **o texto ja vem corrompido da API** - nao e erro de leitura. O aplicativo passou a desfazer
a corrupcao na exibicao, e so quando a releitura forma UTF-8 valido; texto sadio nao e tocado.

Tambem nesta versao:

- Tabelas: copia por celula ou linha com Ctrl+C, e o texto completo em tooltip nas celulas cortadas.
- Upgrade: quota e uso em GB, em colunas separadas; sairam Grupos e Apelidos, que nao diziam nada ali.
- Mensagens: tamanho em KB/MB, data como `31/10/2026 as 15:45`, e Status passou a mostrar a pasta de entrega
  nas recebidas, ja que a API so devolve `Motivo` nas enviadas.
- Periodo com menu pronto (Hoje, Ontem, Ultimos 7 dias, Esta semana, Ultimos 30 e 90 dias, Personalizado). Com
  dominio no lado principal, as opcoes acima de 7 dias ficam desabilitadas e o calendario nao deixa recuar mais.
- **Cursor do campo de varias linhas.** O exemplo ja estava alinhado, mas ao clicar o cursor nascia no meio
  da caixa: `FieldBasics` centraliza o conteudo, e campos de varias linhas herdavam isso. Agora eles usam
  alinhamento no topo, e o cursor nasce na primeira linha, antes da primeira letra do exemplo. O harness
  passou a reprovar qualquer campo de varias linhas que volte a centralizar, e a medir a altura do exemplo
  contra a do texto: **98 campos conferidos, zero divergencias**.
- A correcao do `InputField` foi perdida numa gravacao abortada e so entrou na 1.1.5; a 1.1.4 saiu com ela
  faltando. A verificacao antiga nao pegou porque so media campos que ja usavam `Ui.Hint` - exatamente os
  que tinham sido corrigidos. O harness agora tambem acusa campo com exemplo sobreposto por fora.
- O exemplo dentro dos campos saiu da sobreposicao por fora e passou a viver no template do proprio campo,
  dentro da mesma caixa de padding do texto real - alinhamento por construcao, como na caixa de mensagem do
  WhatsApp: o cursor fica colado antes da primeira letra e o exemplo some ao digitar. O harness de QA passou
  a medir isso: em 32 campos, a origem do exemplo e a do texto coincidem dentro de 0,5 px, nos dois temas e
  nas tres escalas. Um token que vazava como texto no exemplo do relatorio tambem foi corrigido.
- Campos de varias linhas ganharam rolagem horizontal: uma linha longa de CSV travava no fim, sem volta.
- Progresso passou a dizer em qual conta esta, e nao so o contador.
- Desconectar, trocar conexao ou entrar em demonstracao com operacao em andamento agora avisa antes.
- O resumo de gerenciamento de membros passou a dizer, em cada linha, o grupo de onde a pessoa sai ou entra.
- Submenus de Licencas, Relatorios e Grupos passaram a usar o mesmo cartao da tela inicial, com descricao.
- `Display` deixou de depender de dados de cultura instalados: em modo globalizacao invariante, pedir "pt-BR"
  lancaria excecao e derrubaria a formatacao.

## Controle Inteligente de Aplicativos na 1.1.5

O pacote single-file da 1.1.5 foi **recusado** pelo Controle Inteligente, com o mesmo formato do 1.1.4, que
tinha aberto. Confirma que, sem assinatura, o veredito e por arquivo novo e imprevisivel: cada build pode ser
bloqueado. O build em pasta (`release/v1.1-pasta/`, mesmo binario sem empacotar) abre normalmente e serve como
caminho de teste enquanto a assinatura nao existe.

```text
1.1.5 single-file, 162 MB ......... BLOQUEADO
1.1.5 em pasta com DLLs ........... abre
```

## Limites desta validação

- As telas **com resultados preenchidos**, a janela de confirmação e a exportação não foram conferidas
  visualmente: dependem de uma sessão autenticada pela interface. Uso com mouse e teclado por uma pessoa e
  conferência em monitores com DPI diferente também continuam pendentes.
- Os módulos da 1.1 exigem conexão real e **não têm modo de demonstração**: avisam isso na própria tela. A
  demonstração da 1.0.4 continua disponível para as operações antigas.
- O pacote **não é assinado**. Sem assinatura, cada build novo chega sem reputação ao Windows, e o Controle
  Inteligente de Aplicativos pode voltar a recusá-lo em outras máquinas mesmo sem compressão. A assinatura
  corporativa continua sendo a solução definitiva, aqui e no SmartScreen.
- A integração foi validada em **um** ambiente de teste, com um cliente e dois domínios. Produtos, rótulos de
  `accounttype` e volumes de outros clientes podem revelar variações.
- `Tamanho` não é fornecido pela API nos relatórios de mensagens **enviadas**; o campo fica vazio.
- Não há assinatura digital de publicador.

## Verificações novas da 1.1

```text
PASS v1.1: produto da caixa vem de productname, não de accounttype
PASS v1.1: rótulo aceito no accounttype é aprendido de uma caixa que já usa o produto
PASS v1.1: itens que não são endereços de caixa são ignorados ao aprender o rótulo
PASS v1.1: produto sem caixas não inventa rótulo
PASS v1.1: alteração envia o rótulo aprendido e valida contra o nome do catálogo
PASS v1.1: 404 no produto explica divergência de nome em vez de registro não encontrado
PASS v1.1: caixa Exchange orienta abrir chamado, com o nome real da API
PASS v1.1: caixa já no produto do catálogo não é alterada
PASS v1.1: tipos exchange2013 reconhecidos
PASS v1.1: BOM, cabeçalho, duplicata e inválida
PASS v1.1: separador 44
PASS v1.1: separador 59
PASS v1.1: separador 9
PASS v1.1: domínio validado
PASS v1.1: CSV inválido bloqueado
PASS v1.1: limite de entrada
PASS v1.1: arrays de grupo usam chaves por endereço
PASS v1.1: grupo sem compra automática ou aliases
PASS v1.1: endereço não pode injetar chaves de arrays
PASS v1.1: CSV de grupo com aspas e listas
PASS v1.1: quota em bytes calcula 80%
PASS v1.1: upgrade nunca recomenda downgrade
PASS v1.1: upgrade bloqueia conversão Exchange
PASS v1.1: capacidade com espaço no nome
PASS v1.1: SkyMail → Exchange bloqueado
PASS v1.1: Exchange → SkyMail bloqueado
PASS v1.1: licença igual não envia PUT
PASS v1.1: pós-validação obrigatória
PASS v1.1: PUT parcial usa nome do produto, sem productId
PASS v1.1: PUT sem produto confirmado não vira sucesso
PASS v1.1: falta de licença não compra nem repete automaticamente
PASS v1.1: compra apenas com autorização explícita
PASS v1.1: conferência obsoleta exige revisão
PASS v1.1: catálogo só contém produtos retornados
PASS v1.1: disponibilidade usa quantidade contratada e ocupada
PASS v1.1: disponibilidade ausente não vira zero
PASS v1.1: grupo criado e pós-validado
PASS v1.1: grupo existente é ignorado
PASS v1.1: grupo existente não consome confirmação de compra
PASS v1.1: confirmação consumida somente no envio do POST
PASS v1.1: saldo de grupos consultado antes do lote
PASS v1.1: saldo de grupos identifica o cliente dono do estoque
PASS v1.1: produto de grupo ausente mantém saldo desconhecido
PASS v1.1: cliente é identificado mesmo sem produto de grupo
PASS v1.1: grupo inexistente confirmado por 404
PASS v1.1: grupo existente dispensa licença na conferência
PASS v1.1: existência indeterminada continua contando licença
PASS v1.1: resposta de outro grupo não confirma existência
PASS v1.1: rename aguarda e verifica a cada 30 segundos
PASS v1.1: senha somente após novo endereço confirmado
PASS v1.1: exclui somente alias antigo, sem DELETE coletivo
PASS v1.1: timeout de 11 minutos fica pendente e não altera senha
PASS v1.1: polling limitado a 11 minutos
PASS v1.1: bloqueia consulta além de 90 dias
PASS v1.1: filtro de domínio exato
PASS v1.1: rotas proibidas não disponíveis
PASS v1.1: paginação e domínio aplicados juntos
PASS v1.1: tamanho da mensagem recebida vem do JSON embutido
PASS v1.1: mensagem enviada usa DataEnvio quando não há @timestamp
PASS v1.1: login divide intervalo para evitar truncamento
PASS v1.1: login lê User, @timestamp, Motivo e RemoteIP do envelope real
PASS v1.1: array direto em data continua aceito no login
PASS v1.1: JSON tem apenas campos permitidos
PASS v1.1: nome padrão de exportação
PASS v1.1: CSV protege contra fórmulas
PASS v1.1: recusa de licença classificada sem vazar resposta

RESULTADO: 159 passaram; 0 falharam. Nenhuma chamada real à Skymail.
```

## Controle Inteligente de Aplicativos — 09/09/2026

O pacote comprimido da 1.1 foi recusado pelo Controle Inteligente de Aplicativos do Windows 11 com a mensagem
"bloqueou um aplicativo que pode não ser seguro". Medido nesta máquina:

```text
1.0.4 antiga, single-file comprimido, 71 MB ......... abre
1.1 single-file comprimido, 71 MB .................. BLOQUEADO
1.1 single-file sem compressão, 162 MB ............. abre
1.1 pasta com exe e DLLs ........................... abre
```

O arquivo antigo já tinha reputação na máquina; o novo chega desconhecido, e nessa situação o formato pesa: o
bundle **comprimido auto-extraível** é o que o mecanismo recusa. `EnableCompressionInSingleFile` passou a
`false`: o pacote continua sendo **um único .exe**, agora com 162 MB em vez de 71 MB, e abre. Nada foi feito
para escapar da checagem; apenas se deixou de usar um formato que ela trata como suspeito.

A solução definitiva continua sendo **assinar o executável**. Um certificado de assinatura de código válido
resolve o caso em qualquer máquina, com qualquer formato, e também evita o aviso do SmartScreen.

## Pacote da 1.1

```text
Executável de homologação release/v1.1-homologacao/SkyAPI-1.1.5.exe
- Autossuficiente Windows x64, runtime .NET 8 incluído, manifesto asInvoker.
- Publicado com dotnet publish pelo SDK .NET 8.0.424, single-file sem compressão.
- SHA-256: 713d55a7ad91ecdc058782c38b17edea1c17d1662fb5d054b603ba2128a59dc9
- Tamanho: 162.015.257 bytes
- release/SkyAPI.exe continua sendo o pacote 1.0.4 e não foi substituído: a 1.1 ainda depende da
  homologação autenticada descrita nos limites acima.
```

---

# Registro anterior — SkyAPI 1.0.4

Data: 08/09/2026. Substitui a validação da 1.0, refeita no Windows.

## Resultado

- Núcleo compilado pelo compilador C# oficial .NET 8.0.424 com avisos tratados como erros: aprovado.
- Interface WPF compilada contra as referências oficiais Windows Desktop 8.0.30 com avisos tratados como erros: aprovado.
- 74 verificações automatizadas: 74 passaram, zero falhas.
- Executável autossuficiente Windows x64 gerado com o singlefilehost oficial e Microsoft.NET.HostModel.
- Manifesto de execução asInvoker, sem solicitação de administrador.
- Executável aberto e fechado no Windows 11: aprovado. Todas as telas renderizadas e conferidas nos temas claro e escuro, em 90%, 100% e 140%, e em janela estreita.
- Login: o cabeçalho User-Agent exigido pela API é enviado em todas as chamadas, com teste de regressão que reprova a ausência dele.

## Limites desta validação

A interface não foi executada em Windows. Os fluxos visuais, o primeiro lançamento, extração do runtime, escalas de tela/DPI, comportamento de antivírus e integração com a API real ainda dependem de homologação. Não foram utilizados login, token ou dados reais de clientes. Não há assinatura digital de publicador.

O ambiente Linux apresentou uma incompatibilidade do MSBuild com a inspeção de processos. A compilação desta entrega utilizou diretamente o compilador C# oficial (Roslyn), as referências oficiais e o empacotador Microsoft.NET.HostModel, sem alterar código de compiladores ou políticas do ambiente. O projeto inclui o caminho convencional `dotnet publish` para recompilação no Windows. Os programas auxiliares e a verificação estrutural estão na pasta validation.

## Testes executados

```text
PASS Leitura de lista com duas contas
PASS BOM, CRLF e linhas vazias
PASS Exclusão usa DELETE
PASS E-mails inválidos bloqueados
PASS Duplicatas sem distinção de caixa bloqueadas
PASS Lote vazio bloqueado
PASS Endpoint de zona DNS
PASS Domínio com protocolo bloqueado
PASS Domínio com hífen inicial bloqueado
PASS Endpoint de grupos
PASS Endpoint de restauração e URL encoding
PASS Status disabled
PASS Status noaccess
PASS Status active
PASS Status fora da lista bloqueado
PASS Senha vazia bloqueada
PASS Senha preservada
PASS Senha individual preserva espaços e separadores
PASS Senha individual vazia bloqueada
PASS Senha individual sem separador bloqueada
PASS Forçar senha sem alterar a atual
PASS Renomeação com vírgula
PASS Renomeação com ponto e vírgula
PASS Renomeação para si bloqueada
PASS Destinos duplicados bloqueados
PASS Cadeia conflitante bloqueada
PASS CSV com vírgula entre aspas
PASS Atributos separados por ponto e vírgula
PASS Campos vazios não apagados
PASS Cabeçalho mailbox obrigatório
PASS Aliases duplicados bloqueados
PASS Atributos desconhecidos bloqueados
PASS Contagem de colunas validada
PASS Formato de 11 dígitos preserva contrato do script
PASS Telefone de 10 dígitos
PASS Telefone curto bloqueado
PASS Telefone longo não truncado silenciosamente
PASS Aspas desbalanceadas bloqueadas
PASS Limite de tamanho
PASS Aspas escapadas e quebra dentro de campo
PASS Fórmulas desativadas no relatório
PASS JWT HS256 com jti válido
PASS Assinatura HMAC conferida independentemente
PASS JWT malformado rejeitado
PASS JWT vazio rejeitado
PASS JWT sem assinatura segura rejeitado
PASS JWT expirado rejeitado
PASS HTTP 200 confirmado
PASS URL e método corretos
PASS Bearer enviado
PASS Form URL encoding protege senha com símbolos
PASS HTTP 201 confirmado
PASS HTTP 204 confirmado
PASS HTTP 401 interrompe lote
PASS HTTP 403 interrompe lote
PASS HTTP 429 interrompe lote
PASS HTTP 500 interrompe lote
PASS HTTP 302 interrompe lote
PASS HTTP 202 não é conclusão
PASS 404 registrado individualmente
PASS Timeout não provoca reenvio
PASS Falha de rede não presume resultado
PASS Login cria token a partir de jti
PASS Login sem jti recusado
PASS Erro fatal impede próxima chamada
PASS Parada antes do lote não envia nada
PASS Falha ao gravar relatório impede próxima chamada
PASS Sem token não executa

RESULTADO: 74 passaram; 0 falharam. Nenhuma chamada real à Skymail.
```

## Inspeção do pacote

```text
Executável SkyAPI-v1.0.4.exe
- PE Windows x64, interface gráfica: OK
- Manifesto asInvoker (sem administrador): OK
- Runtime .NET 8.0.30 incluído, sem instalação separada: OK
- Ícone da aplicação incorporado: OK
- Abriu e fechou no Windows 11 sem erro: OK
- SHA-256: 677043818c2191333c322d1bd7903f48f81909b41897467e2154fea0d6b00168
- Tamanho: 71.704.227 bytes

A comparação byte a byte dos componentes incorporados (validation/verify_bundle.py) foi feita
apenas na 1.0, cuja montagem gerava a pasta build/stage usada como referência. Este pacote foi
publicado pelo SDK .NET 8.0.424 no Windows com dotnet publish, e não passou por essa conferência.
```
