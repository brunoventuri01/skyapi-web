using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using SkyAPI.Core;
using SkyAPI.Web;

// Confere a versão web contra o núcleo:
//   1. o encaminhamento /api/skymail libera exatamente o que o aplicativo usa, e nada além;
//   2. o adaptador do navegador reescreve a chamada sem perder método, corpo, autorização
//      nem os cabeçalhos de limite de requisições que voltam da API.
// A fonte da verdade da lista de endereços é WEB/api/skymail.js: este teste lê o arquivo.
class Tests {
    static int passed, failed;
    static void Check(bool ok, string name) { if (ok) { passed++; Console.WriteLine("PASS " + name); } else { failed++; Console.WriteLine("FAIL " + name); } }

    // ---------------------------------------------------------------- gateway

    static readonly Dictionary<string, Regex[]> Allowed = new();
    static readonly HashSet<string> AllowedQuery = new(StringComparer.Ordinal);

    /// <summary>Lê as tabelas do encaminhador direto do JavaScript publicado.</summary>
    static void LoadGateway(string file) {
        var source = File.ReadAllText(file);
        var table = Between(source, "const ALLOWED = {", "\n};");
        string? current = null;
        var collected = new Dictionary<string, List<Regex>>();
        foreach (var line in table.Split('\n')) {
            var label = Regex.Match(line, @"^\s*(GET|POST|PUT|DELETE)\s*:");
            if (label.Success) { current = label.Groups[1].Value; collected[current] = new(); continue; }
            if (current == null) continue;
            foreach (var pattern in Literals(line)) collected[current].Add(new Regex(pattern, RegexOptions.CultureInvariant));
        }
        foreach (var entry in collected) Allowed[entry.Key] = entry.Value.ToArray();

        var query = Between(source, "const ALLOWED_QUERY = new Set([", "]);");
        foreach (Match name in Regex.Matches(query, "\"(?<n>[^\"]+)\"")) AllowedQuery.Add(name.Groups["n"].Value);
    }

    /// <summary>Extrai os literais de expressão regular de uma linha de JavaScript, respeitando
    /// as barras dentro de classes de caracteres como [^/].</summary>
    static IEnumerable<string> Literals(string line) {
        for (int i = 0; i < line.Length; i++) {
            if (line[i] != '/') continue;
            var body = new StringBuilder();
            bool inClass = false, closed = false;
            for (int j = i + 1; j < line.Length; j++) {
                char c = line[j];
                if (c == '\\' && j + 1 < line.Length) { body.Append(c).Append(line[j + 1]); j++; continue; }
                if (c == '[') inClass = true;
                else if (c == ']') inClass = false;
                else if (c == '/' && !inClass) { i = j; closed = true; break; }
                body.Append(c);
            }
            if (closed && body.Length > 0) yield return body.ToString();
        }
    }

    static string Between(string text, string start, string end) {
        int a = text.IndexOf(start, StringComparison.Ordinal);
        if (a < 0) throw new InvalidOperationException("Trecho não encontrado em skymail.js: " + start);
        a += start.Length;
        int b = text.IndexOf(end, a, StringComparison.Ordinal);
        if (b < 0) throw new InvalidOperationException("Fim não encontrado em skymail.js: " + end);
        return text[a..b];
    }

    /// <summary>Mesma decisão da função checkPath em skymail.js. Os dois precisam andar juntos.</summary>
    static bool Gateway(string method, string target) {
        var parts = target.Split('?', 2);
        var path = parts[0];
        var query = parts.Length > 1 ? parts[1] : "";
        if (path.Length == 0 || path.Length > 512) return false;
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains("..") || path.Contains("//")) return false;
        if (Regex.IsMatch(path, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)) return false;
        if (!Allowed.TryGetValue(method, out var rules) || !rules.Any(r => r.IsMatch(path))) return false;
        if (query.Length > 1024) return false;
        foreach (var pair in query.Length > 0 ? query.Split('&') : Array.Empty<string>())
            if (!AllowedQuery.Contains(Uri.UnescapeDataString(pair.Split('=')[0].Replace("+", " ")))) return false;
        return true;
    }

    /// <summary>O que o próprio núcleo aceita: RequestAsync (avançado) + ExecuteAsync (lote) + login.</summary>
    static bool CoreAllows(string method, string target) {
        var path = target.Split('?')[0];
        if (method == "POST" && path == "auth/login") return true;
        if (AdvancedPaths.IsAllowed(method, target)) return true;
        // ExecuteAsync aceita qualquer método sobre mailbox/, group/ e dns/ — vindo só do Planner.
        return path.StartsWith("mailbox/", StringComparison.Ordinal)
            || path.StartsWith("group/", StringComparison.Ordinal)
            || path.StartsWith("dns/", StringComparison.Ordinal);
    }

    static void GatewayTests() {
        const string mail = "ana%40empresa.com.br";
        // Tudo que o Planner monta, para todas as operações em lote.
        foreach (Operation op in Enum.GetValues<Operation>()) {
            var plan = Planner.Build(op,
                op == Operation.Attributes ? "mailbox,Nome\nana@empresa.com.br,Ana"
                : op == Operation.RenameAccounts ? "ana@empresa.com.br,nova@empresa.com.br"
                : op == Operation.PasswordDifferent ? "ana@empresa.com.br;Senha!2345"
                : op == Operation.DeleteDns ? "empresa.com.br"
                : "ana@empresa.com.br", "disabled", "Senha!2345");
            Check(plan.Errors.Count == 0, "Planner monta " + op);
            foreach (var item in plan.Items)
                Check(Gateway(item.Method, item.Path), "Encaminhador libera " + op + " → " + item.Method + " " + item.Path);
        }

        // Endereços dos módulos avançados, como AdvancedService os monta.
        (string Method, string Path)[] advanced = {
            ("GET", "mailbox/" + mail),
            ("GET", "mailbox/deleted/" + mail),
            ("GET", "domain/empresa.com.br"),
            ("GET", "domain/empresa.com.br/mail-products"),
            ("GET", "client?page=1&perPage=50"),
            ("GET", "client/1234/product"),
            ("GET", "client/1234/product/77"),
            ("GET", "group/financeiro%40empresa.com.br"),
            ("GET", "report/messages/sent?from=2026-09-01%2000%3A00%3A00&to=2026-09-02%2023%3A59%3A59&limit=100&offset=0&fromEmail=" + mail),
            ("GET", "report/messages/received?from=2026-09-01%2000%3A00%3A00&to=2026-09-02%2023%3A59%3A59&limit=100&offset=0&toEmail=" + mail),
            ("GET", "report/login?user=" + mail + "&from=2026-09-01%2000%3A00%3A00&to=2026-09-02%2000%3A00%3A00&limit=50"),
            ("POST", "auth/login"),
            ("POST", "group"),
            ("PUT", "group/financeiro%40empresa.com.br"),
            ("PUT", "mailbox/" + mail),
            ("PUT", "mailbox/" + mail + "/rename"),
            ("PUT", "mailbox/deleted/" + mail + "/restore")
        };
        foreach (var (method, path) in advanced)
            Check(Gateway(method, path), "Encaminhador libera " + method + " " + path.Split('?')[0]);

        // Recusas: nada fora do que o aplicativo usa pode passar.
        (string Method, string Path, string Why)[] refused = {
            ("GET", "../../etc/passwd", "travessia de diretório"),
            ("GET", "/mailbox/x", "caminho absoluto"),
            ("GET", "mailbox\\x", "barra invertida"),
            ("GET", "https://exemplo.com/mailbox/x", "endereço absoluto"),
            ("GET", "mailbox//x", "barra dupla"),
            ("GET", "dns/empresa.com.br", "leitura de zona DNS não é usada"),
            ("DELETE", "domain/empresa.com.br", "exclusão de domínio não é usada"),
            ("DELETE", "client/1234", "exclusão de cliente não é usada"),
            ("POST", "mailbox", "criação de caixa não é usada"),
            ("PUT", "dns/empresa.com.br", "alteração de zona não é usada"),
            ("PATCH", "mailbox/" + mail, "método fora da lista"),
            ("GET", "client/abc/product", "cliente não numérico"),
            ("GET", "report/login?user=" + mail + "&callback=x", "parâmetro desconhecido"),
            ("GET", "auth/login", "login só por POST"),
            ("GET", "mailbox/" + mail + "/rename", "renomear só por PUT")
        };
        foreach (var (method, path, why) in refused)
            Check(!Gateway(method, path), "Encaminhador recusa " + method + " " + path.Split('?')[0] + " (" + why + ")");

        // Nada liberado pelo encaminhador pode estar fora do que o próprio núcleo aceita.
        var probes = advanced.Concat(refused.Select(r => (r.Method, r.Path)))
            .Concat(new[] { ("PUT", "group"), ("POST", "group/x"), ("DELETE", "mailbox/deleted/" + mail) });
        foreach (var (method, path) in probes)
            Check(!Gateway(method, path) || CoreAllows(method, path),
                "Encaminhador não é mais permissivo que o núcleo em " + method + " " + path.Split('?')[0]);
    }

    // -------------------------------------------------------------- transporte

    static async Task TransportTests() {
        using var listener = new HttpListener();
        int port = FreePort();
        var origin = "http://127.0.0.1:" + port + "/";
        listener.Prefixes.Add(origin);
        listener.Start();

        string? seenUrl = null, seenMethod = null, seenAuth = null, seenType = null, seenDeclared = null, seenBody = null;
        var serving = Task.Run(async () => {
            for (int i = 0; i < 2; i++) {
                var context = await listener.GetContextAsync();
                seenUrl = context.Request.Url!.PathAndQuery;
                seenMethod = context.Request.HttpMethod;
                seenAuth = context.Request.Headers["Authorization"];
                seenType = context.Request.ContentType;
                seenDeclared = context.Request.Headers[BrowserTransport.ContentTypeHeader];
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    seenBody = await reader.ReadToEndAsync();
                context.Response.StatusCode = 200;
                context.Response.Headers["X-RateLimit-Remaining"] = "7";
                context.Response.Headers["X-RateLimit-Reset"] = "1789999999";
                var payload = Encoding.UTF8.GetBytes("{\"success\":true}");
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(payload, 0, payload.Length);
                context.Response.Close();
            }
        });

        using var transport = new BrowserTransport(origin);
        using var invoker = new HttpMessageInvoker(transport);

        // Caminho com consulta e caracteres já escapados precisa chegar inteiro do outro lado.
        const string path = "report/messages/sent?from=2026-09-01%2000%3A00%3A00&fromEmail=ana%40empresa.com.br";
        using (var request = new HttpRequestMessage(HttpMethod.Get, ApiClient.BaseUrl + path)) {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-de-teste");
            using var response = await invoker.SendAsync(request, default);
            Check(response.StatusCode == HttpStatusCode.OK, "Transporte devolve a resposta do encaminhador");
            Check(response.Headers.TryGetValues("X-RateLimit-Remaining", out var left) && left.Single() == "7",
                "Transporte preserva X-RateLimit-Remaining");
            Check(response.Headers.TryGetValues("X-RateLimit-Reset", out _), "Transporte preserva X-RateLimit-Reset");
        }
        Check(seenMethod == "GET", "Transporte preserva o método");
        Check(seenAuth == "Bearer token-de-teste", "Transporte encaminha a autorização");
        var query = seenUrl!.Split('?', 2)[1];
        Check(seenUrl.StartsWith("/api/skymail?path=", StringComparison.Ordinal), "Transporte chama /api/skymail");
        Check(Uri.UnescapeDataString(query["path=".Length..]) == path, "Um único desescape devolve o endereço original");

        // Corpo de formulário: chega byte a byte, com o tipo real declarado à parte.
        using (var request = new HttpRequestMessage(HttpMethod.Put, ApiClient.BaseUrl + "mailbox/ana%40empresa.com.br")) {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["password"] = "A+b c%d&e=f",
                ["mailalternateaddress[antigo@empresa.com.br]"] = "true"
            });
            using var response = await invoker.SendAsync(request, default);
            Check(response.StatusCode == HttpStatusCode.OK, "Transporte envia corpo e recebe resposta");
        }
        Check(seenMethod == "PUT", "Transporte preserva PUT");
        Check(seenType == "application/octet-stream", "Corpo viaja como octet-stream");
        Check(seenDeclared == "application/x-www-form-urlencoded", "Tipo real declarado em " + BrowserTransport.ContentTypeHeader);
        var fields = System.Web.HttpUtility.ParseQueryString(seenBody!);
        Check(fields["password"] == "A+b c%d&e=f", "Senha com +, espaço, % e & preservada");
        Check(fields["mailalternateaddress[antigo@empresa.com.br]"] == "true", "Campo com colchetes preservado");

        // Destino fora da API Skymail não pode ser encaminhado.
        bool blocked = false;
        using (var request = new HttpRequestMessage(HttpMethod.Get, "https://exemplo.com/roubo")) {
            try { using var _ = await invoker.SendAsync(request, default); }
            catch (InvalidOperationException) { blocked = true; }
        }
        Check(blocked, "Transporte recusa destino fora da API Skymail");

        listener.Stop();
        await Task.WhenAny(serving, Task.Delay(1000));
    }

    static int FreePort() {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    static async Task<int> Main(string[] args) {
        var gateway = args.Length > 0 ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "api", "skymail.js"));
        if (!File.Exists(gateway)) { Console.WriteLine("Não encontrei o encaminhador em " + gateway); return 2; }
        LoadGateway(gateway);
        Check(Allowed.Count == 4, "skymail.js declara GET, POST, PUT e DELETE");
        Check(AllowedQuery.Count > 0, "skymail.js declara a lista de parâmetros");

        GatewayTests();
        await TransportTests();

        Console.WriteLine();
        Console.WriteLine(passed + " passaram, " + failed + " falharam.");
        return failed == 0 ? 0 : 1;
    }
}
