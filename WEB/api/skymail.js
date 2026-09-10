// Encaminhador do SkyAPI Web para a API Skymail.
//
// Regras desta função:
//  * só repassa os endpoints e métodos que o aplicativo realmente usa (lista abaixo);
//  * repõe User-Agent e Cache-Control, que o navegador proíbe a página de definir;
//  * devolve os cabeçalhos de limite de requisições sem alteração, porque o núcleo
//    depende deles para espaçar as chamadas;
//  * não grava nem imprime credenciais, corpos, endereços ou tokens em log nenhum;
//  * não segue redirecionamentos: um 3xx volta como 3xx para o núcleo interromper o lote.
//
// Nenhum segredo fica na função. As credenciais são do usuário e só passam de raspão,
// da aba do navegador para a Skymail.

const BASE = "https://api.skymail.net.br/v1/";
const USER_AGENT = "SkyAPI/1.0";           // a API responde 403 sem User-Agent
const MAX_BODY = 512 * 1024;               // os formulários da API são pequenos
const UPSTREAM_TIMEOUT = 55000;

// Endpoints liberados, por método. Espelham SkyAPI.Core (AdvancedPaths e ApiClient).
const ALLOWED = {
    GET: [
        /^mailbox\/[^/]+$/,
        /^mailbox\/deleted\/[^/]+$/,
        /^domain\/[^/]+$/,
        /^domain\/[^/]+\/mail-products$/,
        /^client$/,
        /^client\/[0-9]+\/product$/,
        /^client\/[0-9]+\/product\/[0-9]+$/,
        /^group\/[^/]+$/,
        /^report\/messages\/received$/,
        /^report\/messages\/sent$/,
        /^report\/login$/
    ],
    POST: [
        /^auth\/login$/,
        /^group$/
    ],
    PUT: [
        /^mailbox\/[^/]+$/,
        /^mailbox\/[^/]+\/rename$/,
        /^mailbox\/deleted\/[^/]+\/restore$/,
        /^group\/[^/]+$/
    ],
    DELETE: [
        /^mailbox\/[^/]+$/,
        /^group\/[^/]+$/,
        /^dns\/[^/]+$/
    ]
};

// Parâmetros de consulta usados pelo aplicativo. Qualquer outro é recusado.
const ALLOWED_QUERY = new Set([
    "page", "perPage",                       // client
    "from", "to", "limit", "offset",         // report/messages e report/login
    "fromEmail", "toEmail",                  // report/messages
    "user"                                   // report/login
]);

// Cabeçalhos devolvidos ao navegador. O núcleo usa os de limite para se autorregular.
const PASS_BACK = ["content-type", "x-ratelimit-limit", "x-ratelimit-remaining", "x-ratelimit-reset", "retry-after"];

function refuse(res, status, message) {
    res.statusCode = status;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.end(JSON.stringify({ success: false, message }));
}

function checkPath(method, target) {
    const [path, query = ""] = target.split("?");
    if (path.length === 0 || path.length > 512) return null;
    if (path.startsWith("/") || path.includes("\\") || path.includes("..") || path.includes("//")) return null;
    // Um endereço absoluto ou com autoridade nunca pode virar destino.
    if (/^[a-z][a-z0-9+.-]*:/i.test(path)) return null;
    const rules = ALLOWED[method];
    if (!rules || !rules.some(rule => rule.test(path))) return null;
    if (query.length > 1024) return null;
    for (const pair of query.length ? query.split("&") : []) {
        const key = decodeURIComponent(pair.split("=")[0].replace(/\+/g, " "));
        if (!ALLOWED_QUERY.has(key)) return null;
    }
    return query.length ? path + "?" + query : path;
}

async function readBody(req) {
    // A Vercel pode ter consumido o fluxo antes do handler. Os dois casos são cobertos.
    if (Buffer.isBuffer(req.body)) return req.body;
    if (typeof req.body === "string") return Buffer.from(req.body, "utf8");
    const chunks = [];
    let size = 0;
    for await (const chunk of req) {
        size += chunk.length;
        if (size > MAX_BODY) throw new Error("body-too-large");
        chunks.push(chunk);
    }
    return Buffer.concat(chunks);
}

module.exports = async function handler(req, res) {
    res.setHeader("Cache-Control", "no-store");
    res.setHeader("Referrer-Policy", "no-referrer");
    res.setHeader("X-Content-Type-Options", "nosniff");

    const method = (req.method || "").toUpperCase();
    if (!Object.prototype.hasOwnProperty.call(ALLOWED, method))
        return refuse(res, 405, "Método não permitido por este encaminhamento.");

    let requested;
    try { requested = new URL(req.url, "http://gateway.invalid").searchParams.get("path"); }
    catch { requested = null; }
    if (!requested) return refuse(res, 400, "Requisição sem destino.");

    const target = checkPath(method, requested);
    if (!target) return refuse(res, 403, "Operação fora do escopo permitido para a versão web.");

    let body = null;
    if (method !== "GET") {
        try { body = await readBody(req); }
        catch { return refuse(res, 413, "Conteúdo maior que o permitido."); }
        if (body.length > MAX_BODY) return refuse(res, 413, "Conteúdo maior que o permitido.");
        if (body.length === 0) body = null;
    }

    const headers = {
        "User-Agent": USER_AGENT,
        "Cache-Control": "no-cache",
        "Accept": "application/json"
    };
    const authorization = req.headers["authorization"];
    if (typeof authorization === "string" && authorization.length <= 20000) headers["Authorization"] = authorization;
    if (body) {
        // O tipo real do formulário viaja à parte para nenhum intermediário reinterpretar os campos.
        const declared = req.headers["x-sky-content-type"];
        headers["Content-Type"] = typeof declared === "string" && /^[\w.+-]+\/[\w.+-]+(;.*)?$/.test(declared)
            ? declared : "application/x-www-form-urlencoded";
    }

    const abort = new AbortController();
    const timer = setTimeout(() => abort.abort(), UPSTREAM_TIMEOUT);
    let upstream;
    try {
        upstream = await fetch(BASE + target, {
            method,
            headers,
            body,
            redirect: "manual",     // um 3xx precisa chegar como 3xx ao núcleo
            signal: abort.signal
        });
    } catch (error) {
        clearTimeout(timer);
        // Mensagem fixa: nada do endereço, do corpo ou das credenciais entra em log ou resposta.
        const timedOut = error && error.name === "AbortError";
        return refuse(res, 504, timedOut
            ? "A API excedeu o tempo de espera. Confira o resultado no painel antes de repetir."
            : "Não foi possível acessar a API Skymail.");
    }
    clearTimeout(timer);

    for (const name of PASS_BACK) {
        const value = upstream.headers.get(name);
        if (value !== null) res.setHeader(name, value);
    }
    res.setHeader("Cache-Control", "no-store");
    res.statusCode = upstream.status;

    const payload = Buffer.from(await upstream.arrayBuffer());
    res.end(payload);
};
