# SkyAPI — Skynova

Versão 1.1.12. Windows 10/11 x64.

## Para utilizar

1. Abra SkyAPI-1.1.12.exe com dois cliques. Não é necessário instalar .NET, habilitar PowerShell nem executar como administrador.
2. Para conhecer a interface sem alterações reais, clique em **Experimentar demonstração**. Escolha uma operação, preencha o exemplo e percorra a conferência e a simulação.
3. Para operações reais, abra **Conexão com a API**. Informe o usuário, senha e chave privada do painel, ou um token JWT existente. Se não souber onde obter a chave privada, use o link **Não sabe onde pegar a chave privada? Clique aqui.** logo abaixo do campo: ele mostra o caminho e abre o tutorial oficial da Skymail.
4. Escolha a operação, importe um CSV UTF-8 ou cole os registros. O botão **Salvar modelo** gera o formato necessário.
5. Confira a tabela. Na execução real, digite EXECUTAR na confirmação.
6. Acompanhe cada conta. **Parar após o registro atual** interrompe somente os próximos envios; não desfaz os anteriores.
7. Salve o relatório na pasta desejada. Há também uma cópia automática em `%LOCALAPPDATA%\Skynova\SkyAPI\Relatorios`.

## O que está incluído

- Exclusão e restauração de contas.
- Desabilitar, bloquear acesso ou reativar contas.
- Senha igual para todos, senha individual ou troca obrigatória no próximo login. Com senha igual para todos,
  a senha é conferida enquanto você digita, com **Mostrar senha** e **Gerar senha**.
- Renomeação de contas.
- Atualização de atributos de caixa postal.
- Exclusão de grupos de e-mail e zonas DNS.
- Importação por seleção de arquivo ou arrastar e soltar, modelos, conferência e relatórios.
- Modo de demonstração que não faz chamadas à API.
- Logo oficial e paleta extraídos do site Skynova.
- Tema claro e tema escuro, com troca a qualquer momento.
- Ajuste do tamanho do texto entre 90% e 140%.
- Ícone próprio na janela, na barra de tarefas e no Explorador.

## Módulos acrescentados na versão 1.1

Estes módulos aparecem no menu lateral (**Licenças**, **Relatórios**, **Ferramentas**) e também como cartões
no início da área **Operações em lote** da tela inicial. Todos exigem conexão real: **não existe demonstração
para eles**. Em modo de demonstração ou sem token, o aplicativo avisa e não executa.

- **Alteração de licenças em lote** — consulta cada caixa, mostra produto atual e destino, e envia apenas as
  contas aptas. Permite SkyMail → SkyMail e SkyExchange → SkyExchange, incluindo Basic. Conversões entre as
  duas famílias exigem atendimento da Skymail.
- **Análise para Upgrade** — informe o domínio e o uso mínimo. Com a lista de contas vazia, o aplicativo
  descobre as caixas do domínio pela própria API, sem planilha; preencha a lista apenas para limitar a análise.
  Mostra uso, quota e o menor produto maior da mesma família que comporta a caixa. Considera SkyMail
  (5, 25, 50, 100, 200 e 500 GB) e SkyExchange (Basic 5 e 25 GB; 50, 100 e 200 GB).
  Se o produto sugerido ainda não estiver contratado no painel do cliente, a
  análise diz isso e traz o link de como adicionar. Não altera nada, e a seleção pode ser
  enviada para a tela de alteração de licenças.
- **Criação de grupos em lote** — CSV com cabeçalho obrigatório `grupo,email,membros,escritores,moderadores`.
- **Gerenciar membros de grupos** — inclui ou remove membros, escritores e moderadores de vários grupos de uma
  vez, uma linha por alteração. Remove apenas os endereços indicados; não apaga o grupo.
- **Substituição de colaborador** — valida a senha enquanto você digita: pelo menos 8 caracteres e 3 tipos
  entre maiúsculas, minúsculas, números e símbolos. O botão de execução fica desabilitado até atender à regra.
  Não permite sequências de 3 caracteres em ordem direta, inversa ou de teclado, nem componentes de nomes
  e domínios dos endereços atual e novo com 3 ou mais caracteres (ignorando caixa e acentos).
  O botão **Gerar senha** preenche uma senha aleatória de 10 caracteres que passa pelas mesmas regras.
  Renomeia a caixa, acompanha o processamento por até 11 minutos, troca a
  senha e decide se o endereço antigo continua como apelido. Remove apenas o apelido antigo, nunca a lista toda.
- **Mensagens recebidas** e **Mensagens enviadas** — busca cruzada que o painel não oferece. Cada lado da
  consulta, remetente e destinatário, escolhe entre **um domínio** ou **contas de e-mail**, e você preenche a
  caixa correspondente. Serve para perguntas como "tudo que o domínio X mandou para o meu domínio" ou "o que o
  domínio X mandou para estas três contas". O lado que é seu vai como filtro para a API; o outro lado é
  peneirado pelo próprio aplicativo. Deixe um lado vazio para não filtrar por ele.

Estes módulos **não têm modo de demonstração**: eles consultam e alteram dados reais. Em demonstração, cada um
deles avisa isso na própria tela, em vez de prometer simulação.

Cada tela explica em uma frase o que preencher, mostra um exemplo dentro do próprio campo e tem o botão
**Baixar CSV de exemplo** com um modelo pronto do formato daquela tela.

Todos os módulos aceitam CSV/TXT com vírgula, ponto e vírgula ou TAB, exportam o resultado em CSV e podem ser
interrompidos com **Parar após a chamada atual**. As contas que não chegaram a ser enviadas ficam registradas
como **Não enviada**.

**As operações não travam o aplicativo.** Um lote continua rodando enquanto você navega e usa outro módulo; só
aquele módulo fica bloqueado até terminar. A tela **Processos**, no menu lateral, lista o que está em
andamento, há quanto tempo, quantos registros já saíram, e permite parar cada operação ou abrir a tela dela. O
menu mostra a quantidade de operações em andamento ao lado do nome. Os resultados de cada módulo ficam
guardados enquanto o aplicativo estiver aberto.

Os registros de execução desses módulos ficam em `%LOCALAPPDATA%\Skynova\SkyAPI\Logs`, em JSON, com uma lista
fixa de campos: módulo, conta, operação, resultado e duração. Não gravam senha, token, JTI, chave privada nem
o corpo das respostas da API.

## Licenças e contratação

A conferência mostra quantas licenças o lote exige e quantas a API informa como disponíveis. Quando a API
informa `amount` e `count` do produto, o saldo é a diferença entre os dois, nunca menor que zero; quando não
informa, o aplicativo mostra **não informadas** e jamais supõe zero.

O estoque pertence ao **cliente**, não ao domínio — um cliente pode ter vários domínios, e o usuário que faz o
login não precisa ter nenhum deles. Na criação de grupos, a conferência descobre o cliente de cada domínio, soma
o mesmo estoque uma única vez e desconta os grupos que já existem, verificados antes da execução. Grupos cuja
existência a API não confirma continuam contando licença.

Na alteração de licenças, a conferência também mostra o **nome que será enviado à API**. Isso é necessário
porque o nome do catálogo e o nome que a caixa aceita nem sempre coincidem: o catálogo traz
`SkyMail Premium 500GB`, e a alteração só é aceita com `SkyMail 500GB`. O aplicativo descobre esse nome lendo
uma caixa que já usa o produto, em vez de deduzir a partir do texto. Se nenhuma caixa usar o produto ainda, ele
envia o nome do catálogo e avisa na conferência.

Se a API recusar por falta de licença, o aplicativo interrompe aquela conta, junta as recusas e pede uma
confirmação explícita, dizendo quantas licenças serão contratadas. **Nenhuma compra acontece sem essa
confirmação.** Autorizada a continuação, cada conta ou grupo é reenviado com a confirmação de compra, porque a
API contrata **uma licença por chamada**, no momento em que o grupo é criado ou o produto da caixa é alterado —
comportamento medido contra a API em 09/09/2026. Ao terminar, o aplicativo informa quantas confirmações de
compra foram enviadas.

## Conexão com a API

A chave privada da organização fica no painel Skymail: entre em https://painel.skymail.net.br com um
usuário administrador, clique na seta no canto superior direito, escolha **Configurações** e abra a aba
**Interface API**. Na documentação da Skymail essa chave aparece como SECRET KEY. O tutorial oficial está
em https://ajuda.skymail.com.br/tutoriais/localizar-interface-api/ e o aplicativo abre esse endereço pelo
link ao lado do campo.

O aplicativo envia o cabeçalho `User-Agent: SkyAPI/1.0` em todas as chamadas. A API Skymail recusa com HTTP 403 qualquer requisição sem esse cabeçalho, inclusive o login — foi o que impedia a autenticação até a versão 1.0.2. Se a API passar a exigir outro identificador, altere `ApiClient.UserAgent` e confirme contra a API antes de distribuir.

A chave privada não vai para a API no login: ela apenas assina localmente o token depois que o painel aceita usuário e senha. Por isso as mensagens de erro dessa etapa falam de usuário, senha e permissão, e não da chave.

## Aparência

O rodapé do menu lateral tem o ajuste de **Tamanho do texto** (90%, 100%, 110%, 125% e 140%) e o botão **Modo escuro / Modo claro**. As duas escolhas valem imediatamente e ficam gravadas em `%LOCALAPPDATA%\Skynova\SkyAPI\preferencias.ini`, de modo que a próxima abertura já vem no formato escolhido. Apagar esse arquivo devolve o padrão: tema claro em 100%.

O ajuste de tamanho amplia a interface inteira — textos, ícones, campos e espaçamentos —, mantendo as proporções em qualquer resolução ou escala do Windows. A janela é responsiva: abaixo de aproximadamente 880 px úteis, os botões do topo passam para uma linha própria e a grade de operações reduz de três para duas colunas e depois para uma.

O estado da conexão aparece em três lugares ao mesmo tempo: a etiqueta ao lado do botão do topo, o cartão do rodapé lateral e, na tela **Conexão com a API**, uma faixa que diz se você está conectado, em demonstração ou desconectado, com o botão para desfazer o estado atual.

## Cuidados de funcionamento

Senhas, tokens e chave privada ficam apenas na memória da sessão. Não há telemetria nem servidor intermediário. Relatórios não contêm segredos nem respostas brutas da API, mas contêm os endereços administrados; trate-os como dados internos.

O executável inclui o runtime .NET 8.0.30 e extrai componentes automaticamente para a área temporária do usuário na primeira execução. É um único arquivo para download, não um programa que não grava nenhum arquivo. Ele é publicado **sem compressão interna**, com cerca de 162 MB. Essa configuração não garante a liberação pelo Controle Inteligente de Aplicativos: a versão 1.0.4 fornecida pelo usuário era comprimida e também não tinha assinatura. O Windows avalia a confiança de cada arquivo; os registros locais mostram bloqueio de arquivos da 1.1.5 e da 1.1.6 por requisitos de assinatura/confiança. Precisa de acesso a `https://api.skymail.net.br/v1/` para uso real. Não possui assinatura digital de publicação; o Windows ou as políticas da empresa podem impedir a abertura. A distribuição deve utilizar assinatura de código de um provedor confiável, incluindo os componentes próprios e o executável final.

Esta versão não tem atualização automática, persistência de token, reenvio automático ou desfazer operações. O formato de um token é verificado localmente; sua assinatura e suas permissões são verificadas pela API durante as chamadas.

Timeouts, falhas de conexão e respostas pendentes exigem conferência no painel antes de repetir. A API pode ter recebido a alteração mesmo que o aplicativo não receba a confirmação. Erros de autenticação, redirecionamentos, limites e falhas de servidor interrompem o restante do lote.

## Formatos

CSV UTF-8, limite de 5 MB e 10.000 registros. Listas simples: um e-mail ou domínio por linha, sem cabeçalho. Renomeação: endereço atual,endereço novo (também aceita ponto e vírgula). Senhas individuais: endereço;senha — todo o conteúdo após o primeiro ponto e vírgula compõe a senha, inclusive espaços.

Módulos da 1.1: as listas de contas dispensam cabeçalho — uma conta por linha, ou várias por linha separadas
por vírgula, ponto e vírgula ou TAB. Endereços com `[` ou `]` são recusados, porque a API usa colchetes para
identificar itens de listas nos campos enviados.

Criação de grupos exige o cabeçalho `grupo,email,membros,escritores,moderadores`, com as cinco colunas em toda
linha. Você pode listar várias pessoas na mesma célula separando com `|`, **ou repetir o e-mail do grupo em
várias linhas**, uma pessoa por linha — o aplicativo soma tudo no mesmo grupo. Colunas de pessoas podem ficar
vazias. Exemplo:

```text
grupo,email,membros,escritores,moderadores
Financeiro,financeiro@empresa.com.br,ana@empresa.com.br|bia@empresa.com.br,ana@empresa.com.br,
Suporte,suporte@empresa.com.br,carlos@empresa.com.br,,
Suporte,suporte@empresa.com.br,daniela@empresa.com.br,,
```

Gerenciar membros exige o cabeçalho `grupo,acao,papel,endereco`, uma linha por alteração. A ação é `adicionar`
ou `remover`; o papel é `membro`, `escritor` ou `moderador`. As alterações do mesmo grupo são enviadas juntas.
Exemplo:

```text
grupo,acao,papel,endereco
financeiro@empresa.com.br,remover,membro,ana@empresa.com.br
suporte@empresa.com.br,adicionar,moderador,carlos@empresa.com.br
```

Atributos exigem `mailbox` no cabeçalho. Campos permitidos: Nome, Email secundario, Celular, Telefone residencial, Telefone comercial, Empresa, Unidade, Departamento, Cargo e Ramal. Os nomes técnicos equivalentes também são aceitos. Campos vazios não apagam valores. Telefones exigem 10 ou 11 dígitos, sem +55.

## Diferenças intencionais em relação ao script

- Registros inválidos, duplicatas e colunas desconhecidas impedem a execução do lote inteiro na conferência.
- Telefones inválidos bloqueiam a conferência em vez de serem ignorados após outras alterações. Números longos não são truncados.
- Senhas individuais preservam espaços e caracteres especiais.
- Todos os fluxos produzem relatório; fórmulas de planilha são neutralizadas nos campos exportados.
- HTTP 202 é tratado como pendente, não como conclusão. Não há reenvio automático.
- A recuperação de contas é solicitada à API; o aplicativo não promete recuperação fora das condições do serviço.

## Homologação pendente

Os testes automatizados e as verificações do pacote estão em VALIDACAO.md. A versão 1.1.0 foi compilada no
Windows 11 com o SDK .NET 8 e passa 159 verificações automatizadas, nenhuma delas chamando a Skymail.
O relatório de login foi conferido contra a API real em uma sondagem somente de leitura; ver VALIDACAO.md.

Os módulos novos foram executados contra a API real em um ambiente de teste, incluindo alteração de licença com
e sem saldo, criação de grupos com contratação, substituição de colaborador completa e os três relatórios. As
divergências encontradas na API estão descritas e corrigidas em VALIDACAO.md. As onze telas foram renderizadas e
conferidas nos dois temas, em 90%, 100% e 140% e em janela estreita.

Continua pendente para a 1.1:

- Conferência das telas **com resultados preenchidos**, da janela de confirmação e da exportação, que dependem
  de uma sessão autenticada pela interface.
- Uso simultâneo de vários módulos em operação longa, com a tela de Processos aberta.
- Uso com mouse e teclado por uma pessoa e conferência em monitores com DPI diferente.
- Validação em mais de um cliente: produtos, rótulos de produto e volumes podem variar.

Esta é uma entrega funcional candidata à homologação, não uma certificação de produção.

Antes de distribuir aos clientes, execute o modo de demonstração no Windows; confira importação, navegação, relatório e parada; depois valide autenticação e cada operação em contas, grupos e domínios de teste autorizados, inclusive casos de falha. Assine o EXE pelo processo corporativo quando aprovado.

## Assinatura e distribuição

O executável é assinado por `compilar.cmd`, que chama `tools/assinar.ps1` logo depois de compilar. Sem assinatura,
um binário recém-compilado não abre em máquina com o Smart App Control ligado: o Windows recusa com
`0x800711C7`, "uma política de Controle de Aplicativo bloqueou este arquivo".

O certificado usado hoje (`CN=SkyAPI Code Signing`, autoassinado, em `SkyAPI-CodeSigning.cer`) resolve apenas
onde a sua raiz está instalada — a máquina de desenvolvimento. **Ele não serve para entregar a clientes:** no
computador deles o Windows não conhece essa raiz, o SmartScreen apresenta "editor desconhecido" e o Smart App
Control, ligado por padrão em instalação limpa do Windows 11, bloqueia o programa.

Para distribuir é preciso um certificado de code signing publicamente confiável. Um certificado **EV** dá
reputação imediata no SmartScreen e evita o aviso já no primeiro download; um **OV** custa menos, mas a
reputação é construída com o tempo e os primeiros clientes ainda veem o aviso. Desde 2023 a chave precisa ficar
em hardware, então é token físico ou serviço de assinatura em nuvem.

Com o certificado novo, informe o thumbprint dele:

```
powershell -ExecutionPolicy Bypass -File tools/assinar.ps1 -Arquivo SkyAPI-1.1.12.exe -Thumbprint <NOVO>
```

ou altere o valor padrão dentro de `tools/assinar.ps1`.

A mesma política bloqueia as DLLs recém-compiladas de `tests/`. Assine a pasta de saída antes de rodar os
testes, ou compile os mesmos fontes com outro nome de assembly fora da pasta do projeto.

## Código-fonte

Requer SDK .NET 8 atualizado no computador de desenvolvimento. Execute `compilar.cmd`, ou:

```text
dotnet publish src/SkyAPI.Desktop/SkyAPI.Desktop.csproj -c Release -r win-x64 --self-contained true -o release
```

Testes, sem conexão externa (159 verificações):

```text
dotnet run --project tests/SkyAPI.Tests/SkyAPI.Tests.csproj
```

Interface: WPF em C#, montada em código: `Program.cs` (telas), `Theme.cs` (paleta e preferências), `Ui.cs` (estilos dos controles) e `Logo.cs` (marca em vetor, que acompanha o tema). Núcleo: biblioteca .NET independente da interface.

O ícone `src/SkyAPI.Desktop/Assets/skyapi.ico` é gerado a partir do SVG da marca por `tools/gerar-icone.ps1`; refaça-o apenas se a marca mudar. O teste de transporte usa um HttpMessageHandler falso: nenhum teste acessa a Skymail.

Referências da marca: https://skynova.com.br/ e https://skynova.com.br/wp-content/uploads/2025/06/Logo-Skynova-Web_Prancheta-1.svg. Paleta do SVG: #263570, #0059A7, #1E4180, #009DDB, #184893, #00BAEB, #0074BC. Base funcional: api-requests.ps1 do ZIP fornecido pelo solicitante.

## Novidades da 1.1.12

Em **Gerenciar senhas**, a modalidade "Mesma senha para todas as contas" passou a ter a mesma conferência da
Substituição de colaborador: pelo menos 8 caracteres e 3 tipos entre maiúsculas, minúsculas, números e símbolos,
sem sequências de 3 caracteres em ordem direta, inversa ou de teclado. O indicador mostra a contagem enquanto
você digita e **Conferir registros** fica desabilitado até a senha atender à regra — uma senha fraca falharia em
todas as contas do lote, e barrar antes evita gastar milhares de chamadas à API para nada.

**Mostrar senha** revela o que foi digitado, para conferir antes de entregar. **Gerar senha** preenche uma senha
aleatória de 10 caracteres que já passa pelas regras. A política de senha da organização continua valendo no
envio: ela é aplicada pela API e pode ser mais exigente que essa conferência.

Diferença proposital em relação à Substituição de colaborador: lá a senha também não pode conter partes dos
endereços envolvidos, porque são dois endereços conhecidos. No lote a mesma senha vale para todas as contas, e
não há um par de endereços a confrontar.

A ordem dos campos ficou igual à do aplicativo nas duas versões: primeiro a modalidade, depois a senha, por
último os registros. Trocar de modalidade limpa senha e registros, porque o formato dos registros muda.

## Novidades da 1.1.11

Na alteração de licenças, consulte os produtos da conexão, selecione o produto de destino e informe as contas. Não é necessário preencher domínio. A consulta identifica os clientes acessíveis ao usuário de painel; quando houver mais de um, o cliente aparece junto ao produto. As contas do lote devem pertencer ao cliente escolhido, podendo ter domínios diferentes.

Sucesso recebe destaque verde; falhas, interrupções e resultados parciais exigem conferência. A barra e o botão de parar aparecem apenas durante a execução. Ao retornar pelo menu, o status encerrado fica oculto; os detalhes e a exportação continuam disponíveis em Processos → Abrir (última operação de cada módulo nesta sessão).

Contratação de licenças: alteração de produto, criação de grupos e restauração de caixas conferem a necessidade antes de enviar. A confirmação informa o saldo e o limite de licenças adicionais. Ao autorizar, a compra é enviada junto com a operação que consome a licença. Se a API não informar o saldo, o limite máximo é apresentado explicitamente antes da execução.

O limite da API é compartilhado entre os módulos: intervalo mínimo de 550 ms por requisição, com espera pela renovação quando a API informar que as requisições disponíveis acabaram. A espera aparece na tela. O aplicativo não reenvia alterações automaticamente após HTTP 429.
