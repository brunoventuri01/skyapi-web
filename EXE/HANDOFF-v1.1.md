# SkyAPI v1.1 - handoff atualizado

## Versao 1.1.5 - revisao de uso (09/09/2026)

Pacote: `release/v1.1-homologacao/SkyAPI-1.1.5.exe`, SHA-256
`713d55a7ad91ecdc058782c38b17edea1c17d1662fb5d054b603ba2128a59dc9`, 162.015.257 bytes. **159 testes**, zero
avisos, 99 imagens de QA sem achados. Detalhamento em VALIDACAO.md.

Principais pontos de codigo desta rodada:

1. `FieldError` em `AdvancedUi` carrega o controle culpado; o `Guard` mostra faixa vermelha no topo e destaca o
   campo. Toda validacao passou a usar `Fail(mensagem, campo)`.
2. Grupos: a conferencia guarda `groupClientOf` e `groupStock` fora do bloco de resumo, e a execucao decide por
   grupo se manda `confirm_purchase`. Some a leva de falhas seguida de nova execucao.
3. `Display` novo em `AdvancedModels`: `Size`, `Gigabytes`, `Percent`, `When` e `Repair`. O `Repair` desfaz o
   texto corrompido que **vem da API** - medido: resposta em ASCII com escapes unicode, sem charset, e a
   leitura bate byte a byte. So aplica quando a releitura forma UTF-8 valido.
4. O exemplo do campo virou a propriedade anexada `Ui.Hint`, renderizada dentro de `FieldTemplate`, na mesma
   celula do `PART_ContentHost`. Sobreposicao por fora nao enxerga o padding interno e nunca alinhava. O
   harness mede a diferenca das origens e reprova acima de 0,5 px.
5. `Table` ganhou `SelectionUnit=CellOrRowHeader`, `ClipboardCopyMode` e tooltip por celula. `FieldTemplate`
   parou de fixar as barras de rolagem em Hidden e passou a seguir o proprio TextBox.
6. Seletor de periodo com presets; com dominio no lado principal, opcoes acima de 7 dias desabilitadas e
   `DisplayDateStart` limitado. As datas continuam editaveis - desabilitar deixava o campo cinza no tema
   escuro - e mexer nelas troca o preset para Personalizado.
7. `ConfirmDisconnect` cobre desconectar, trocar conexao e entrar em demonstracao.
8. `OperationCard` extraido de `Home` e reaproveitado em `SubMenu`, usado pelos tres submenus.

### Pendencias

- Conferir na interface, ja conectado: telas com resultados, janela de confirmacao, exportacao, copia da
  tabela, tooltip, e dois modulos rodando ao mesmo tempo com a tela de Processos aberta.
- Validar em mais de um cliente.
- Publicar em `release/SkyAPI.exe` quando isso estiver conferido.


## Versão 1.1.2 — operações paralelas e busca cruzada (09/09/2026)

Pacote: `release/v1.1-homologacao/SkyAPI-1.1.2.exe`, SHA-256
`b2ea7a033783390229ea2f4be12ceb6c7118760748a3ceafbbad4a838da923f0`, 162.000.921 bytes. **154 testes passando**,
zero avisos de compilação, 99 imagens de QA em `validation/qa-v1.1/` sem achados.

1. **Operações não bloqueiam mais o aplicativo.** O antigo `busy` global foi substituído, nos módulos da 1.1,
   por um `JobState` por módulo em `AdvancedUi`: resultados, status, progresso e pedido de parada vivem na
   janela, não na página. A página se inscreve no `Changed` da tarefa e se desinscreve por `pageCleanup`,
   invocado no início de `Clear`. Só o módulo em execução fica bloqueado; o botão de conexão fica desabilitado
   enquanto qualquer tarefa roda. `busy` continua existindo para os fluxos da 1.0.
2. **Tela Processos** (`Processes`, chave de menu `jobs`): lista tarefas em andamento e concluídas, com início,
   duração, contagem de registros, status, botão de parar e de abrir o módulo. Atualiza por `DispatcherTimer`
   de 1 s, encerrado em `Clear`. A contagem aparece no menu por `UpdateJobsBadge`.
3. **Busca cruzada de mensagens.** Cada lado escolhe domínio (um) ou contas (várias). **Medido: domínio só
   funciona no parâmetro principal da API**; no secundário ela devolve zero sem erro. Então `Messages` recebe
   `(sent, primary, counterparts, …)`, manda só o lado próprio para a API e filtra o resto localmente — uma
   chamada por valor do lado próprio. Lado principal por domínio limita o período a 7 dias.
4. Tempo limite do HttpClient: 40 → **60 segundos**.

### Pendências

- Conferir na interface, com sessão autenticada, as telas com resultados preenchidos, a janela de confirmação,
  a exportação e o uso simultâneo de módulos com a tela de Processos aberta.
- Validar em mais de um cliente.
- Publicar em `release/SkyAPI.exe` quando isso estiver conferido.


## Versão 1.1.1 — revisão de usabilidade (09/09/2026)

Pacote: `release/v1.1-homologacao/SkyAPI-1.1.1.exe`, SHA-256
`d5cfcb674f29d0b3901c5b34a4808abca172c2db35fb9a001b08f08c1f4a290f`, 161.992.217 bytes. **150 testes passando.**
Daqui em diante o nome do executável carrega a versão, a pedido do solicitante.

Feito nesta rodada, a partir do retorno de uso do solicitante:

- **Bug real:** o template de ComboBox não tinha `PART_EditableTextBox`, então "Uso mínimo (%)" aparecia vazio
  e não aceitava digitação. Corrigido em `Ui.ComboStyle`.
- **Análise para Upgrade** deixou de exigir planilha: com a lista vazia, `AdvancedService.Mailboxes` descobre as
  caixas do domínio cruzando `domain/{d}/mail-products` com os `items` de cada produto do cliente. Conferido ao
  vivo: 15 caixas em `brunoventuri.skydemo.com.br`, sem grupos.
- **Novo módulo "Gerenciar membros de grupos"** (`groupedit`): `PUT group/{mail}` com `rfc822member[x]=true`
  inclui e valor vazio remove só aquele endereço — medido na API. CSV `grupo,acao,papel,endereco`.
- **Criação de grupos** aceita uma pessoa por linha, repetindo o e-mail do grupo (antes virava duplicata).
- **Senha visível** na substituição de colaborador, com botão Mostrar/Ocultar.
- Orientação em texto por tela, exemplo dentro dos campos, botão **Baixar CSV de exemplo**, aviso de que a
  lista de produtos só traz o que está contratado no painel, com link para a documentação.
- **Retirados** os relatórios de login e de caixas, por decisão do solicitante. `Logins`/`LoginRecord` seguem
  no núcleo, testados, sem tela — reexpor é só recriar a entrada de menu.

### Pendente do mesmo retorno, ainda NÃO feito

1. **Operações não bloqueantes e menu de processos.** Hoje `busy` trava a navegação inteira durante um lote.
   O pedido é travar só a operação em andamento, permitir navegar e usar outros módulos, e ter uma lista dos
   processos em execução. Exige estado por módulo e um registro de tarefas; é a maior pendência.
2. **Relatórios de mensagens: busca cruzada.** O pedido é consultar "tudo que o domínio X enviou para o
   domínio Y" e "domínio X para as contas Y, Z, H", nos dois sentidos. A API aceita **domínio** em `fromEmail`
   e `toEmail` (medido), então dá para fazer; falta redesenhar a tela e ajustar `Messages` para aceitar
   domínio dos dois lados. Consultas por domínio em janelas longas estouram o tempo limite de 40 s.


## Atualização de 09/09/2026 — retomada após a parada nos 10%

Esta seção é o estado ATUAL e prevalece sobre tudo o que vem abaixo. A implementação continua **não
homologada**: o que falta depende de chamadas autenticadas reais, que não foram feitas.

### O que foi feito nesta retomada

1. **Saldo de grupos por cliente (pendência 2 — resolvida no que dá para resolver sem API real).**
   `AdvancedService` ganhou `GroupBalance(domain)`, que devolve `GroupStock(ClientId, Available)`, e
   `GroupExists(email)`, que devolve `false` no 404, `true` quando a API confirma o mesmo endereço e `null`
   quando não dá para afirmar. `AvailableGroups` continua existindo e agora delega a `GroupBalance`.
   A conferência de grupos em `AdvancedUi` passou a: consultar a existência de cada grupo antes do lote,
   descontar os que já existem, continuar contando os de existência indeterminada, agrupar os domínios pelo
   `clientId` e somar o mesmo estoque **uma única vez**. Domínios sem `clientId` são listados à parte.

2. **Continuação após recusa de licença de grupo (pendência 1 — RESOLVIDA e demonstrada).** A primeira tentativa
   desta sessão manteve a flag única por lote e releu o saldo do cliente; o item 7 mostra que essa regra estava
   errada e o comportamento final é o descrito lá: **uma confirmação de compra por item autorizado**. A releitura
   de saldo foi removida, porque após a compra o saldo volta a zero (compra e consumo ocorrem na mesma chamada).

3. **Duplicata deixou de esconder "Não enviada" (pendência 3).** O `finally` do `Guard` usava
   `rows.Any(r=>r.Account==a)`, e a linha "Ignorada — Conta duplicada" tem o mesmo endereço da conta aceita,
   suprimindo o registro da conta que nunca foi processada. Agora existe um `HashSet` `processedAccounts`
   alimentado somente quando a conta realmente produz linha de resultado.

4. **Continuação não duplica mais linha (pendência 5 antiga).** A troca de linha virou a função local
   `RecordContinuation`, usada nos três caminhos (sucesso, pulo por saldo zero e exceção).

5. **Resultados sobrevivem à navegação e à troca de tema (pendência 3).** As tabelas eram locais da página e
   morriam a cada re-render. Agora a janela guarda o conteúdo e cada montagem cria uma coleção nova semeada com
   ele — uma coleção por grade, sem view compartilhada. Trocar de módulo ou iniciar nova execução limpa. O texto
   de status também é preservado.

6. **Documentação atualizada.** `LEIA-ME.md` está na 1.1.0, com os módulos novos, formatos (inclusive o CSV de
   grupos), o comportamento medido de licenças/contratação e a lista de homologação pendente. `VALIDACAO.md` virou a validação da 1.1 com o registro da 1.0.4 preservado no fim.

7. **Integração completa contra a API real**, com credenciais de teste e o domínio
   `brunoventuri.skydemo.com.br`, em ambiente declarado descartável e sem cobrança pelo solicitante. Houve
   escritas e contratações, autorizadas. Credenciais só por variável de ambiente, nunca em arquivo; harnesses
   descartáveis fora do projeto. **Cinco divergências reais da API foram encontradas e corrigidas:**

   a. **`report/login` nunca funcionou.** Resposta é `data:{total_results,messages}`, não array em `data`;
      campos são `User`, `@timestamp`, `Motivo`, e `RemoteIP`/`Protocolo`/`Tipo`/`Hostname` só existem dentro do
      campo `message` (JSON embutido). O código lia minúsculas com busca sensível a caixa, então toda consulta
      dava "Resposta de login inválida.". Corrigido em `LoginRecord`; `LoginRow` ganhou `Protocol` e a tela e o
      CSV ganharam a coluna Protocolo.

   b. **`accounttype` da caixa não usa os nomes do catálogo** (`SkyMail 500GB` para `SkyMail Premium 500GB`,
      `Exchange 50GB` para `SkyExchange 50GB`). Só `productname` traz o nome do catálogo. `MailboxInfo.Product`
      passou a vir de `productname`; isso conserta o bloqueio de Exchange com a mensagem certa, a detecção de
      "já usa este produto" e a pós-validação da alteração, que antes marcaria Pendente e pararia o lote.

   c. **O PUT exige o rótulo da caixa, não o nome do catálogo nem o productId.** Medido: `SkyMail Premium 500GB`
      → 404 com saldo; `SkyMail 500GB` → 200; `302` → 404. `ProductLabel` aprende o rótulo lendo o
      `accounttype` de uma caixa que já usa o produto (`client/{id}/product/{id}` devolve `items`), sem
      adivinhar transformação de nome. A conferência mostra o nome que será enviado; um 404 agora explica a
      divergência.

   d. **`confirm_purchase=true` contrata exatamente UMA licença**, a consumida pela própria chamada. Medido com
      saldo 1 e 4 grupos: primeiro criado, três recusados; a flag em um criou aquele e os outros dois seguiram
      recusados. **A regra antiga de flag única por lote contratava 1 das N autorizadas.** Agora a flag vai em
      cada item autorizado, o diálogo diz "até N licenças, uma por grupo/conta", e o resultado informa quantas
      confirmações foram enviadas. Reconferido: 4 grupos pedidos, 4 criados.

   e. **Relatórios de mensagens: data e tamanho vinham vazios.** `Tamanho` só existe dentro do `message`
      embutido das recebidas; as enviadas não têm `@timestamp` (usam `DataEnvio`/`timestamp`). Corrigido em
      `MessageRecord`. Enviadas não têm tamanho na API; o campo fica vazio.

   Também validados ao vivo: leitura de caixa, catálogo, saldo por cliente, alteração de licença nos dois
   sentidos com e sem saldo, sugestão de upgrade, e **substituição de colaborador completa** (renomeação
   confirmada em ~400 s, senha alterada, endereço antigo mantido como apelido e os outros apelidos preservados).

   Comportamentos da API registrados: o estoque é do cliente e não do domínio (cliente 40958 abrange
   `brunoventuri.skydemo.com.br`, `thiagosousa.skydemo.com.br` e `nexusecosystem.com.br`), o que confirma a
   soma por `clientId`; `amount` pode ser menor que `count`; o filtro por endereço nos relatórios só acha caixas
   existentes, remetentes de caixas excluídas aparecem só na consulta por domínio; consulta de mensagens por
   domínio em janela longa estoura o tempo limite de 40 s.

### Validação e executáveis ATUAIS

- Build Release: **zero erros, zero avisos**.
- Suíte: **140 passaram; zero falhas**. Saída em `validation/test-results-v1.1.txt`. Seis verificações novas
  cobrem `GroupBalance`/`GroupExists`.
- Nenhuma verificação automatizada chama a Skymail. As chamadas reais estão no item 7, com escritas e
  contratações autorizadas em ambiente de teste.
- `release/v1.1-homologacao/SkyAPI.exe` — single-file autossuficiente da 1.1, para homologação.
  SHA-256 `87db0e8f887fed0758c87c69603ea7b5716593b00f36f1f6314aea46d0b81b69`, 71.747.631 bytes.
- `release/SkyAPI.exe` **continua sendo a 1.0.4** e não foi substituído de propósito: a 1.1 não passou por
  homologação autenticada. Substituir só depois disso.
- `validation/v1.1-preview` foi removida: estava desatualizada e o pacote acima a substitui.

### QA visual e demonstração — feitos em 09/09/2026

O QA visual foi feito com o harness `validation/qa-harness.cs`, fora do produto: ele instancia a janela real,
navega por cada tela, aplica tema e escala, salva PNG e vasculha a árvore visual. **91 imagens** em
`validation/qa-v1.1/`, cobrindo as onze telas nos dois temas, em 90/100/140% e em janela estreita, mais três em
demonstração. Verificações automáticas de texto cortado e de rolagem horizontal fora das tabelas: **zero
ocorrências**. Um defeito real foi encontrado e corrigido: os seletores de data usavam a moldura padrão do
Windows e ficavam brancos no tema escuro; agora seguem a paleta (`Ui.DatePickerStyle`/`DatePickerFieldStyle`).

Demonstração: em vez de simulação inventada, os módulos novos mostram o aviso **"Não é possível demonstrar este
módulo"**, com a orientação de sair da demonstração. `Clear` ganhou o parâmetro opcional `demoNote` para isso, e
o harness reprova qualquer tela nova que volte a prometer simulação.

### Onde continuar exatamente

1. **Telas com resultados preenchidos, janela de confirmação e exportação** não foram conferidas visualmente:
   dependem de sessão autenticada pela interface. Uso com mouse e teclado por uma pessoa e monitores com DPI
   diferente também faltam.
2. **Validar em mais de um cliente**: produtos, rótulos de `accounttype` e volumes podem variar. O aprendizado
   do rótulo depende de existir uma caixa usando o produto; sem isso o app envia o nome do catálogo e avisa.
3. Publicar em `release/SkyAPI.exe` quando 1 estiver resolvido. Hoje o pacote da 1.1 fica em
   `release/v1.1-homologacao/`. **Controle Inteligente de Aplicativos:** o bundle comprimido era recusado;
   `EnableCompressionInSingleFile` virou `false` e o pacote passou a abrir, ao custo de 162 MB em vez de 71 MB
   (medições em VALIDACAO.md). Assinar o executável é a solução definitiva e permitiria voltar ao comprimido.
4. **Limpeza no ambiente de teste** (o solicitante disse que é descartável, mas ficou registro): grupos criados
   `skyapi.teste1.20260909145411`, `skyapi.teste2.20260909145411`, `skyapi.teste1..4.20260909145603`, todos em
   `@brunoventuri.skydemo.com.br`; e a caixa `renomeada21238@brunoventuri.skydemo.com.br` foi renomeada para
   `skyapi.subst.20260909150100@brunoventuri.skydemo.com.br`, com senha nova e o endereço antigo como apelido.

Comandos de build/testes seguem os do fim deste documento; o SDK em `.tools/dotnet` está pronto. Não retomar
pelos cartões da tela inicial nem pela implementação dos módulos: estão prontos e conferidos.

---

## Registro anterior (histórico; estados antigos abaixo foram superados)
# SkyAPI v1.1 — ponto de continuação

Atualizado em 09/09/2026. Trabalho interrompido a pedido do usuário. A implementação ainda não está finalizada nem pronta para declarar a versão entregue.

## Resposta sobre a tela inicial

Os novos módulos foram adicionados ao menu lateral. NÃO foram adicionados cartões/atalhos à área central de **Operações em lote**. O método `Home()` e o catálogo `cards` em `src/SkyAPI.Desktop/Program.cs` ainda mostram as operações anteriores. Próxima alteração solicitada: integrar ali os novos acessos, preservando os fluxos antigos.

## Código alterado

- `src/SkyAPI.Core/ApiClient.cs`: somente a declaração virou `partial`, para reutilizar o HttpClient e o token existentes. Login, geração JWT, User-Agent e cache em memória foram preservados.
- `src/SkyAPI.Core/AdvancedApi.cs` (novo): transporte dos novos módulos, lista de rotas permitidas, intervalo mínimo de 550 ms entre chamadas, leitura JSON, classificação de falta de licença e erros sem mostrar respostas brutas.
- `src/SkyAPI.Core/AdvancedModels.cs` (novo): CSV/TXT com autodetecção de vírgula, ponto e vírgula e TAB; validação de contas e grupos; modelos de quota/produto; regras de licença e sugestão de upgrade; exportação CSV; logs JSON com lista explícita de campos permitidos.
- `src/SkyAPI.Core/AdvancedService.cs` (novo): consulta individual; catálogo de produtos; disponibilidade por cliente/produto; troca de licença com pré e pós-validação; grupos com membros/escritores/moderadores; substituição com polling de 30 segundos e prazo de 11 minutos; relatórios recebidos/enviados paginados; login com divisão de intervalos quando atinge 50 registros.
- `src/SkyAPI.Desktop/AdvancedUi.cs` (novo): telas dos módulos, importação, conferência, progresso, interrupção, exportações e transferência de seleção do upgrade para licenças.
- `src/SkyAPI.Desktop/Program.cs`: classe MainWindow virou partial; versão 1.1.0; novos itens do menu lateral. Cartões centrais antigos não alterados.
- `src/SkyAPI.Desktop/AssemblyInfo.cs` e `SkyAPI.Desktop.csproj`: versão atualizada para 1.1.0.
- `tests/SkyAPI.Tests/AdvancedTests.cs` (novo): testes dos novos componentes.
- `tests/SkyAPI.Tests/Program.cs`: passou a chamar AdvancedTests.Run(Check).

## Validação realmente executada

1. Build Release do Desktop: passou com zero erros e zero avisos.
2. Suíte de testes: **116 passaram, zero falhas**, sem chamadas reais à Skymail. Inclui os 74 testes existentes e 42 verificações novas.
3. Build local aberto no Windows. Foram inspecionados o menu principal, o submenu Licenças e a tela Alteração em lote. Não houve teste completo das demais telas ou execução autenticada pela interface.
4. Depois desses testes foram feitos ajustes adicionais em `Program.cs` e `AdvancedUi.cs`; **esses últimos ajustes ainda não foram recompilados nem testados**.

O último ajuste gravado antes da interrupção:

- Encurtou os rótulos laterais para Relatórios e Ferramentas, evitando truncamento.
- Desabilitou o botão Parar fora de uma operação.
- Acrescentou registros Não enviada para contas restantes quando uma operação interrompe antes de processá-las.
- Acrescentou a quantidade de licenças necessárias ao resumo de grupos.

## Executáveis e SDK

- **Nenhum publish da v1.1 foi executado.** `release/SkyAPI.exe` continua sendo o executável anterior.
- O binário aberto para inspeção foi `src/SkyAPI.Desktop/bin/Release/net8.0-windows/win-x64/SkyAPI.exe`. Ele não contém necessariamente os últimos ajustes de código.
- O SDK anterior não estava mais no caminho registrado pelos arquivos obj. Foi baixado o SDK oficial .NET 8.0.424, com SHA-512 conferido pelos metadados oficiais, e extraído em `.tools/dotnet`.
- `.tools/dotnet-sdk.zip` também ficou na pasta. Não distribuir a pasta .tools junto do aplicativo.
- A pasta do projeto não é um repositório Git; não há commit ou diff de Git disponível.

## Pendências para continuar

1. Adicionar os atalhos novos na área central de Operações em lote. Reutilizar `Advanced("licenses")`, `Advanced("upgrade")`, `Advanced("received")`, `Advanced("sent")`, `Advanced("groups")`, `Advanced("replace")`, `Advanced("mailboxes")` e `Advanced("login")` conforme a organização pretendida. Não inserir essas operações no Planner antigo sem necessidade.
2. Recompilar e executar testes após os últimos ajustes. Fazer revisão dos fluxos completos pela interface, inclusive confirmação e exportação.
3. Revisar compra de grupos: hoje a UI coleta recusas, pede UMA confirmação e envia `confirm_purchase=true` apenas na primeira continuação de grupo. Os demais grupos seguem sem essa flag. Se a API disponibilizar só uma licença nessa chamada, os demais continuarão com falha. **A contratação de todas as X licenças em uma única chamada não está demonstrada nem resolvida.** Não transformar isso em compra silenciosa ou contrariar a regra de envio único.
4. Revisar o resumo de grupos: informa a quantidade necessária, mas ainda não consulta saldo antes da execução. Licenças de caixas já consultam saldo por `amount - count` quando os campos estão presentes; ausência é mostrada como desconhecida.
5. Revisar cancelamento/erro no segundo passo de compra e registros duplicados: o catch de ApiFailure na continuação pode acrescentar outra linha sem substituir a primeira. Também conferir se a flag única de compra deve ser consumida quando o grupo já passou a existir entre as consultas.
6. Confirmar os campos efetivamente retornados pelos relatórios em testes de integração controlados. O código lê `De`, `Para`, `Assunto`, `Tamanho`, `Motivo`, `@timestamp`; a documentação não exemplifica Tamanho. Não afirmar que assunto/tamanho estão preenchidos na API real sem verificação.
7. Revisar rotulagem de consultas parcialmente concluídas caso uma página posterior falhe: as linhas já coletadas ficam exportáveis, mas o resultado da conta pode aparecer como Falhou em vez de Parcial.
8. Os novos módulos exigem conexão e não têm demonstração implementada; a demonstração antiga permanece. Não há persistência das novas tabelas entre navegação/troca de tema. Conferir UX desejada antes da entrega.
9. Conferir todas as telas em tamanhos e temas diferentes, inclusive tabela de upgrade/caixas, formulários de mensagens e substituição, e janela de confirmação.
10. Atualizar documentação de uso e validação; gerar o exe final somente após revisão/testes. LEIA-ME.md e VALIDACAO.md ainda descrevem a versão anterior.

## Comandos de continuação (PowerShell, raiz do projeto)

```powershell
& '.tools/dotnet/dotnet.exe' build src/SkyAPI.Desktop/SkyAPI.Desktop.csproj --no-restore -c Release
& '.tools/dotnet/dotnet.exe' run --project tests/SkyAPI.Tests/SkyAPI.Tests.csproj -c Release --no-restore
# Somente após finalizar e validar:
& '.tools/dotnet/dotnet.exe' publish src/SkyAPI.Desktop/SkyAPI.Desktop.csproj -c Release -r win-x64 --self-contained true -o release
```

## Contratos consultados e restrições preservadas

Fonte: https://api.skymail.net.br/doc.html (a documentação antiga do Apiary aponta para essa página).

- Handoff do usuário prevalece: `accounttype` recebe NOME DO PRODUTO; PUT é parcial, sem displayname/password quando não alterados.
- GET individual fornece `mailBoxQuotaSize.mailquotasize` e `used` em bytes.
- Grupos: `POST /group`; campos multivalorados enviados como `rfc822member[email]=true`, `rfc822sender[email]=true`, `rfc822moderator[email]=true`.
- Remoção específica: `PUT /mailbox/{novo}` com `mailalternateaddress[endereco-antigo]=` (valor vazio). NÃO usar DELETE da coleção de aliases, pois remove todos.
- Relatórios de mensagens usam fromEmail/toEmail/from/to/limit/offset; login não documenta offset, por isso usa divisão de períodos.
- Nenhuma busca dinâmica GET /mailbox foi adicionada. Upgrade e relatório de caixas trabalham com listas fornecidas pelo usuário.
- Conversões envolvendo Exchange, aliases de grupo e restauração de mensagens em lote não foram adicionados.
- Logs dos novos módulos ficam em `%LOCALAPPDATA%/Skynova/SkyAPI/Logs`, sem senhas, JWT, JTI, secret ou corpo bruto das respostas.

## Instruções adicionais do usuário

- Autorizou trabalhar na pasta e executar ferramentas/comandos necessários. A sessão passou a acesso irrestrito, sem pedidos de aprovação técnica.
- Pediu aviso quando restarem 10% de uso, com descrição exata do que foi feito e onde parou. A última consulta antes desta pausa mostrou 64% restantes na janela de 5 horas e 78% na semanal; consultar novamente ao retomar, pois não são valores permanentes.
- Pausa atual foi solicitada pelo usuário, não por atingir o limite de uso.

