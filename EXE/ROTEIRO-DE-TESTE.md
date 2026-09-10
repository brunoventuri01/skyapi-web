# Roteiro de teste — SkyAPI 1.1.5

Feito para o ambiente `brunoventuri.skydemo.com.br`, com o estado real conferido em 09/09/2026 às 20:47.
Cada bloco tem o que colar e o que esperar. Abra `release/v1.1-homologacao/SkyAPI-1.1.5.exe` e conecte à API.

> Criar grupo **contrata licença**: o saldo de Grupo de E-mail está em **0**. No seu ambiente de teste não há
> cobrança, mas é isso mesmo que o teste 3 exercita.

---

## 1. Alteração de licenças em lote

**Domínio**

```text
brunoventuri.skydemo.com.br
```

Clique em **1. Consultar produtos do cliente**. A lista deve trazer 5 produtos: SkyMail 5GB, SkyMail Premium
500GB, SkyExchange 50GB, SkyExchange Basic 25GB e SkyMail 25GB.

Escolha **SkyMail 25GB** e cole em **Contas**:

```text
skyapi.subst.20260909150100@brunoventuri.skydemo.com.br
comercial4@brunoventuri.skydemo.com.br
```

**Esperado:** a conferência mostra a primeira como `SkyMail 5GB → SkyMail 25GB` e a segunda como **Bloqueada**,
com o texto de abrir chamado (é uma caixa SkyExchange). Confira também a linha **Nome enviado à API** — para
SkyMail 25GB ela é igual ao nome do catálogo.

Para ver o caso em que o nome do catálogo **não** é o que a API aceita, repita escolhendo
**SkyMail Premium 500GB**: a conferência deve mostrar `Nome enviado à API: SkyMail 500GB`.

---

## 2. Análise para Upgrade

**Domínio**

```text
brunoventuri.skydemo.com.br
```

Deixe **Contas vazio** e use **Uso mínimo 1**.

**Esperado:** ele encontra **16 caixas** sozinho, sem planilha. A tabela traz Quota e Uso em GB — por exemplo
`50 GB` e `38,53 GB · 77,1%` para `matheus2@`. Não deve haver colunas de Grupos e Apelidos, e nenhuma linha
"Falhou" (os grupos do domínio não entram na busca).

Passe o mouse na coluna **Detalhes** para ver o texto inteiro, e selecione uma célula e faça **Ctrl+C**.

---

## 3. Criar grupos em lote — com contratação

Cole no CSV (troque `NNN` por um número qualquer se quiser repetir o teste depois):

```text
grupo,email,membros,escritores,moderadores
Teste Lote NNN,teste.loteNNN@brunoventuri.skydemo.com.br,eduardo@brunoventuri.skydemo.com.br|matheus2@brunoventuri.skydemo.com.br,eduardo@brunoventuri.skydemo.com.br,
Teste Lote NNN,teste.loteNNN@brunoventuri.skydemo.com.br,edmilson@brunoventuri.skydemo.com.br,,
Teste Outro NNN,teste.outroNNN@brunoventuri.skydemo.com.br,comercial4@brunoventuri.skydemo.com.br,,
```

**Esperado na conferência:**

- **Grupos informados: 2** — as duas primeiras linhas são o mesmo grupo e somam membros (3 no total).
- **Já existentes: 0**
- Uma linha por cliente com o saldo, e a mensagem em destaque:
  **FALTAM 2 LICENÇAS. Ao executar, elas serão contratadas automaticamente, uma por grupo.**

**Esperado ao executar:** os dois grupos saem como **Sucesso** já na primeira passada, sem nenhuma linha
"Falhou" antes. No fim, o rodapé informa quantas confirmações de compra foram enviadas.

---

## 4. Gerenciar membros de grupos

```text
grupo,acao,papel,endereco
financeiro.teste@brunoventuri.skydemo.com.br,remover,membro,matheus2@brunoventuri.skydemo.com.br
financeiro.teste@brunoventuri.skydemo.com.br,adicionar,moderador,eduardo@brunoventuri.skydemo.com.br
suporte.teste@brunoventuri.skydemo.com.br,remover,membro,edmilson@brunoventuri.skydemo.com.br
comercial.teste@brunoventuri.skydemo.com.br,adicionar,escritor,comercial4@brunoventuri.skydemo.com.br
```

**Esperado na conferência:** três grupos, e cada alteração em uma linha dizendo o grupo por extenso, tipo
`remover membro matheus2@brunoventuri.skydemo.com.br de financeiro.teste@brunoventuri.skydemo.com.br`.

**Esperado ao executar:** três **Sucesso**. Confira no painel que só os endereços citados mudaram — os demais
membros de cada grupo continuam lá.

Para devolver ao estado anterior, rode:

```text
grupo,acao,papel,endereco
financeiro.teste@brunoventuri.skydemo.com.br,adicionar,membro,matheus2@brunoventuri.skydemo.com.br
financeiro.teste@brunoventuri.skydemo.com.br,remover,moderador,eduardo@brunoventuri.skydemo.com.br
suporte.teste@brunoventuri.skydemo.com.br,adicionar,membro,edmilson@brunoventuri.skydemo.com.br
comercial.teste@brunoventuri.skydemo.com.br,remover,escritor,comercial4@brunoventuri.skydemo.com.br
```

---

## 5. Substituição de colaborador

- **Conta atual:** `skyapi.subst.20260909150100@brunoventuri.skydemo.com.br`
- **Novo endereço:** `skyapi.subst.teste2@brunoventuri.skydemo.com.br`
- **Nova senha:** escolha uma e clique em **Mostrar senha** para conferir e copiar
- **Manter apelido:** Sim

**Esperado:** a renomeação leva alguns minutos (no teste anterior, cerca de 400 s de um limite de 660). Enquanto
roda, o progresso deve dizer a conta: `skyapi.subst... → skyapi.subst.teste2... · Processando renomeação…`.

**Aproveite para testar o que mais importa:** com isso rodando, vá em **Licenças** ou **Relatórios** e use outro
módulo. O aplicativo não pode travar. Abra **Processos** no menu — deve mostrar a substituição em andamento,
com tempo correndo, e o menu deve exibir **Processos (1)**.

Ainda com ela rodando, tente **Desconectar** na tela de Conexão: deve aparecer um aviso de que há operação em
andamento antes de deixar você desconectar.

---

## 6. Mensagens recebidas — busca cruzada

**Remetente:** deixe *Domínio* e **vazio**.
**Destinatário:** *Domínio*

```text
thiagosousa.skydemo.com.br
```

**Período:** Últimos 7 dias.

**Esperado: 2 mensagens**, ambas de `noreply@email.openai.com`, com data no formato `04/09/2026 às 13:45`,
tamanho `61,4 KB` e Status `INBOX`.

Agora repita preenchendo o **Remetente** como *Domínio* `email.openai.com` — deve dar as mesmas 2. Troque para
`gmail.com` e deve dar **0**, provando que o filtro do outro lado funciona.

---

## 7. Mensagens enviadas

**Remetente:** *Domínio*

```text
brunoventuri.skydemo.com.br
```

**Período:** Últimos 7 dias.

**Esperado: 1 mensagem**, de `substituicao.skyapi.20260909112351@` para `assistente.gerencia4@propstarter.com.br`,
com Status começando em `(250 2.0.0 Ok`. O Tamanho fica **vazio** — a API não devolve tamanho nas enviadas.

---

## 8. Período e o limite de 7 dias

Ainda em Mensagens enviadas, com o **Remetente em Domínio**, abra o menu **Período**: *Últimos 30 dias* e
*Últimos 90 dias* devem estar **desabilitados**, e o calendário não deve deixar escolher antes de 7 dias atrás.

Troque o Remetente para **Contas de e-mail** e cole:

```text
substituicao.skyapi.20260909112351@brunoventuri.skydemo.com.br
```

Com o **Destinatário vazio**, as opções de 30 e 90 dias devem **voltar a ficar disponíveis**.

**Novo na 1.1.7 — o limite vale para os dois lados.** Ainda com o Remetente em Contas de e-mail, ponha o
**Destinatário** em *Domínio* e digite `gmail.com`: as opções de 30 e 90 dias devem **desabilitar de novo**,
já ao digitar, sem precisar mexer no menu de opção. Apague o domínio e elas voltam. Vale igual em Mensagens
recebidas, com os lados trocados: destinatário nas suas contas e remetente `gmail.com` também limita a 7 dias.

---

## 8b. Lista de produtos de destino (1.1.7)

Em **Licenças → Alteração em lote**, clique em **1. Consultar produtos do cliente**. Além da
mensagem "N produtos retornados pela API", a lista **Produto de destino** precisa **abrir e deixar escolher**.
Troque o domínio depois de consultar: a lista deve esvaziar e travar de novo até nova consulta.

Em **Licenças → Análise para Upgrade**, confira a coluna **Uso**: o percentual sai com **um único** sinal de
porcentagem (`1,49 GB · 3%`), e não `3%%`.

---

## 8c. Upgrade sugerido fora do painel do cliente (1.1.7)

Em **Licenças → Análise para Upgrade**, use um domínio cujo cliente **não tenha** o próximo tamanho contratado
no painel — por exemplo uma caixa de 50 GB acima do limite, num cliente sem produto de 100 GB.

**Esperado:** a coluna **Produto sugerido** traz `SkyMail Premium 100GB` e os Detalhes dizem que ele *não está
contratado no painel do cliente*, pedindo para adicionar por lá e consultar de novo. Não deve mais aparecer
"Nenhum upgrade SkyMail compatível disponível".

Selecione essa conta e clique em **Enviar para Alteração de Licenças**: deve aparecer o aviso de que o produto
ainda não está no painel, em vez de abrir um lote que travaria na conferência.

Depois de adicionar o produto no painel Skymail e rodar de novo, a mesma conta deve passar a dizer apenas
"Upgrade sugerido; nenhuma alteração realizada" — e aí o envio para a alteração em lote funciona.

---

## 8d. Relatório grande, com corte da API (1.1.7)

Em **Mensagens recebidas**, com uma conta que receba bastante e um período de 7 dias, execute e acompanhe o
texto de progresso: ele agora mostra a **fatia de período** que está sendo consultada (`04/09 00:00 a 06/09
12:00 · N registros consultados`). Quando a API corta a entrega, o aplicativo divide o período sozinho.

**Esperado:** a linha de resultado diz "N mensagens encontradas", **sem** o aviso de que a API parou antes do
fim. Se o aviso ainda aparecer, ele agora diz que o corte persistiu *mesmo com o período dividido* — anote o
"X de Y registros anunciados" e me mande.

---

## 9. Erros — o teste que mais me interessa

Faça de propósito, um de cada vez, e veja se **fica evidente**:

**a)** Em Mensagens enviadas, com o Remetente em *Domínio*, cole um e-mail:

```text
renato@lrimoveis.com.br
```

Esperado: faixa vermelha no topo dizendo que não é um domínio e sugerindo trocar a opção, com o campo
destacado em vermelho.

**b)** Em Criar grupos em lote, cole um CSV sem cabeçalho:

```text
Financeiro,financeiro@empresa.com.br,ana@empresa.com.br,,
```

Esperado: faixa vermelha dizendo qual cabeçalho é obrigatório.

**c)** Em Alteração de licenças, clique em **Conferir e executar** sem consultar os
produtos. Esperado: faixa vermelha mandando consultar o domínio e escolher o destino, com a lista destacada.

**d)** Em Análise para Upgrade, apague o percentual e execute. Esperado: faixa vermelha sobre o Uso mínimo.

---

## 10. Coisas de acabamento para olhar de passagem

- O exemplo dentro dos campos: o cursor deve ficar colado antes da primeira letra, e o exemplo some ao digitar.
- Cole uma linha bem longa no CSV de grupos e confira que dá para voltar ao início com a barra horizontal.
- Troque para o **Modo claro** e confira que os resultados continuam na tela.
- Nos submenus Licenças, Relatórios e Grupos, os cartões devem ter descrição.

---

## Estado do ambiente em 09/09/2026

Caixas SkyMail (as que aceitam troca de licença):

```text
skyapi.subst.20260909150100@brunoventuri.skydemo.com.br      SkyMail 5GB
substituicao.skyapi.20260909112351@brunoventuri.skydemo.com.br  SkyMail 25GB
testedecolaborador2@brunoventuri.skydemo.com.br              SkyMail 25GB
renomeada1238@brunoventuri.skydemo.com.br                    SkyMail Premium 500GB
```

As demais 12 caixas do domínio são SkyExchange e devem sair como **Bloqueada** na alteração de licença.

Grupos com membros, bons para o teste 4: `financeiro.teste@`, `comercial.teste@`, `suporte.teste@`,
`equipe.teste@`, `diretoria.teste@`, `externo.teste@`, `operacoes.teste@`, `todos.teste@`.

Saldo de licenças de Grupo de E-mail: **0** (cliente 40958).


## Versão 1.1.12

Em **Gerenciar senhas**, com "Mesma senha para todas as contas":

- Campo vazio: o indicador mostra `0/8 caracteres`, `0/3 tipos` e **Conferir registros** fica desabilitado.
- `12345678901234`: continua desabilitado, com `1/3 tipos`.
- `Rmvn2958`: indicador verde com "Senha válida" e o botão libera.
- **Mostrar senha** revela o texto; editar no campo revelado revalida; **Ocultar senha** preserva o que foi digitado.
- **Gerar senha**: preenche 10 caracteres e libera o botão.
- Trocar para "Senha diferente por conta" ou "Exigir troca no próximo login": o campo de senha some, os registros
  são limpos e o botão volta a ficar habilitado.
- Ordem dos campos: modalidade, senha, registros — a mesma na versão web.

## Versão 1.1.11

- Licenças: não deve haver campo Domínio. Ordem: consultar produtos, selecionar destino, informar contas.
- Conexão com vários clientes: conferir identificação do cliente junto ao produto; contas de outro cliente bloqueiam a conferência.
- Sucesso: faixa verde; falha ou resultado parcial: destaque de atenção, sem sucesso verde.
- Sair durante execução e retornar: operação continua com progresso e parada disponíveis.
- Sair depois de concluir e retornar pelo menu: sem painel de status antigo. Em Processos, Abrir recupera status, detalhes e exportação.
