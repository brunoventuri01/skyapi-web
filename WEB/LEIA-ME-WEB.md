# SkyAPI Web

Versão para navegador do assistente de operações da API Skymail, publicada na Vercel.
O aplicativo Windows continua em `../EXE` e não foi alterado.

---

## Como funciona

```
navegador                                  Vercel                      Skymail
┌──────────────────────────────┐      ┌──────────────────┐      ┌────────────────┐
│ Blazor WebAssembly           │      │ /api/skymail     │      │ api.skymail    │
│  • SkyAPI.Core (o mesmo      │ ───► │  função Node     │ ───► │  .net.br/v1/   │
│    núcleo do EXE)            │      │  lista fixa de   │      │                │
│  • BrowserTransport          │ ◄─── │  endpoints       │ ◄─── │                │
│  • credenciais só em memória │      │  sem estado      │      │                │
└──────────────────────────────┘      └──────────────────┘      └────────────────┘
```

O núcleo C# (`WEB/Core`) é **o mesmo do aplicativo Windows**: as mesmas validações, os mesmos
textos, o mesmo controle de limite de requisições e as mesmas regras de contratação. Só a
camada de tela mudou (Blazor no lugar do WPF) e só o transporte mudou (`BrowserTransport`).

O encaminhamento existe por uma razão técnica: o navegador **proíbe** a página de definir
`User-Agent`, e a API Skymail responde **403** para qualquer chamada sem esse cabeçalho.
A função da Vercel repõe `User-Agent` e `Cache-Control`, devolve os cabeçalhos de limite
sem alteração e recusa qualquer endereço fora da lista usada pelo aplicativo.

### Credenciais

Usuário, senha, chave privada e token existem **apenas na memória da aba**. Nada é gravado
em `localStorage`, em cookie, na função da Vercel ou em log. A chave privada assina o JWT
dentro do navegador e é apagada da memória logo depois. Fechar ou atualizar a aba encerra a
sessão e apaga o histórico — baixe os resultados antes de sair.

Só o tema e o tamanho do texto ficam gravados no navegador.

### Uma conexão por vez

Enquanto uma aba está conectada, outra aba do mesmo navegador recusa a conexão. O cadeado usa
a Web Locks API, com reserva em `localStorage` quando ela não está disponível.

### Processos

Os processos continuam ao navegar entre módulos. Fechar ou atualizar a aba encerra a sessão —
o navegador avisa antes, enquanto houver operação em andamento.

---

## Estrutura

```
WEB/
  Client/            aplicação Blazor WebAssembly
    Main.razor         telas, navegação e formulários
    JobResults.razor   tabelas de resultados, paginação e exportação
    WebSession.cs      conexão, processos, confirmações e execução dos módulos
    BrowserTransport.cs reescreve a chamada do núcleo para /api/skymail
    wwwroot/           index.html, css/app.css, sky.js, logotipos
  Core/              núcleo C# compartilhado com o EXE (não editar só de um lado)
  api/skymail.js     encaminhamento para a API Skymail (função da Vercel)
  scripts/           build local e build da Vercel
  tests/             conferência do encaminhamento e do transporte
  vercel.json        build, cabeçalhos de segurança e rota do /api
```

> **Atenção:** `WEB/Core` e `EXE/src/SkyAPI.Core` precisam continuar iguais. Ao corrigir uma
> regra, copie a alteração para os dois lados e rode os testes dos dois.

---

## Compilar e conferir na máquina

Pré-requisito: SDK .NET 8. O deste repositório está em `..\.tools\dotnet\dotnet.exe` e os
scripts o encontram sozinhos.

```
scripts\publicar.cmd     compila para WEB\dist (o mesmo que a Vercel faz)
scripts\testar.cmd       sobe a interface em http://localhost:5005
```

`testar.cmd` serve **só a interface**: fora da Vercel não existe `/api/skymail`, então apenas
a navegação, o visual e o **modo demonstração** funcionam. Para exercitar a API de verdade na
máquina é preciso Node.js e a CLI da Vercel:

```
npm i -g vercel
cd WEB
vercel dev
```

### Testes automatizados

```
..\.tools\dotnet\dotnet.exe run --project tests\SkyAPI.Web.Tests
```

Confere, lendo o próprio `api/skymail.js`, que o encaminhamento libera todos os endereços que
o núcleo gera, recusa tudo o mais e nunca é mais permissivo que o próprio núcleo; e que o
`BrowserTransport` preserva método, corpo, autorização e os cabeçalhos de limite.

Os testes do núcleo continuam em `..\EXE\tests`.

---

## Publicar na Vercel

### 1. Criar o projeto

Na Vercel: **Add New → Project**, escolha o repositório e configure:

| Campo | Valor |
|---|---|
| **Root Directory** | `WEB` |
| Framework Preset | Other |
| Build Command | (deixe o de `vercel.json`) |
| Output Directory | (deixe o de `vercel.json`) |

O `Root Directory` é o único ajuste obrigatório na interface. Sem ele a Vercel não encontra
`vercel.json`, `api/` nem os scripts.

### 2. O que o build faz

`scripts/vercel-build.sh` baixa o SDK .NET 8 para dentro do build (a imagem da Vercel não traz
.NET), publica o cliente Blazor e deixa o resultado em `dist/`. A função `api/skymail.js` é
publicada à parte pela Vercel, no runtime Node.

O primeiro build leva alguns minutos por causa do download do SDK.

### 3. Variáveis de ambiente

Nenhuma. A função não guarda segredo nenhum — as credenciais são sempre do usuário.

### 4. Alternativa: enviar já compilado

Se o download do SDK no build da Vercel for um problema, dá para compilar aqui e publicar só o
resultado:

1. `scripts\publicar.cmd`
2. remova `WEB/dist/` do `.gitignore` e faça commit da pasta `WEB/dist`
3. em `vercel.json`, troque `"buildCommand"` por `"echo dist ja compilado"`

---

## Segurança

* **Lista fixa de endereços.** `api/skymail.js` só encaminha os endpoints e métodos que o
  aplicativo usa, e só os parâmetros de consulta que ele monta. Caminho com `..`, `\`, `//`,
  barra inicial ou endereço absoluto é recusado antes de qualquer chamada.
* **Sem log de credencial.** A função não imprime endereço, corpo, token nem cabeçalho de
  autorização. As mensagens de erro são fixas.
* **Sem redirecionamento.** Um 3xx da API volta como 3xx e o núcleo interrompe o lote, como no
  aplicativo Windows.
* **Cabeçalhos de limite preservados.** `X-RateLimit-Limit`, `X-RateLimit-Remaining`,
  `X-RateLimit-Reset` e `Retry-After` chegam intactos ao núcleo, que se autorregula com eles
  (intervalo mínimo de 550 ms entre chamadas).
* **Cabeçalhos da página.** `vercel.json` define CSP, `X-Content-Type-Options`,
  `Referrer-Policy`, `X-Frame-Options` e `Permissions-Policy`. `connect-src` é `'self'`: a
  página não fala com nenhum outro endereço.
* **Corpo intacto.** O formulário viaja como `application/octet-stream` com o tipo real em
  `X-Sky-Content-Type`, para nenhum intermediário reinterpretar campos como
  `mailalternateaddress[antigo@empresa.com.br]` ou senhas com `+`, `%` e `&`.

---

## Diferenças em relação ao aplicativo Windows

| | Windows | Web |
|---|---|---|
| Histórico entre sessões | mantido em disco | só na aba aberta |
| Relatório em arquivo | grava direto na pasta | baixado como CSV pelo navegador |
| Preferências | `%LOCALAPPDATA%\Skynova\SkyAPI` | `localStorage` do navegador |
| Tamanho do texto | 90% a 140% | 90% a 140% |
| Atualizar a página | não se aplica | encerra a sessão (com aviso) |

Regras de negócio, validações, mensagens, conferência prévia e autorização de contratação são
as mesmas nos dois.

---

## Se der problema

| Sintoma | Onde olhar |
|---|---|
| Build da Vercel falha no download do SDK | use a alternativa "enviar já compilado" |
| `403 Operação fora do escopo permitido` | o endereço não está na lista de `api/skymail.js`; rode `tests/SkyAPI.Web.Tests` |
| `403` vindo da Skymail no login | usuário sem permissão de administrador na Interface API |
| Página em branco depois de publicar | veja o console do navegador; se acusar CSP, ajuste `Content-Security-Policy` em `vercel.json` |
| "Já existe uma conexão SkyAPI em outra aba" | feche a outra aba ou desconecte-a |
| `504` no meio de um relatório longo | a consulta passou de 60 s; use um período menor |
