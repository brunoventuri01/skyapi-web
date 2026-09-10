using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkyAPI.Core;

// Uses the existing HttpClient and session token; does not implement another login.
public sealed partial class ApiClient {
    public async Task<ApiReply> RequestAsync(string method, string path,
        IReadOnlyDictionary<string,string>? fields = null, CancellationToken ct = default) {
        if (!HasToken) throw new InvalidOperationException("Configure a conexão antes de executar.");
        if (!AdvancedPaths.IsAllowed(method, path)) throw new InvalidOperationException("Operação fora do escopo da versão 1.1.");
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            if (fields?.Count > 0) request.Content = new FormUrlEncodedContent(fields);
            try {
                using var response = await SendControlledAsync(request, ct).ConfigureAwait(false);
                var code = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                JsonElement json = default;
                if (!string.IsNullOrWhiteSpace(body)) {
                    try { using var doc = JsonDocument.Parse(body); json = doc.RootElement.Clone(); }
                    catch (JsonException) { return new(code, false, default, false, "Resposta inválida da API.", true); }
                }
                var explicitFailure = JsonValue.Text(json, "success").Equals("False", StringComparison.OrdinalIgnoreCase);
                var success = response.IsSuccessStatusCode && !explicitFailure;
                // Classify known licence/password refusals into fixed messages; never display raw responses.
                var message = JsonValue.Text(json, "message");
                var needs = !success && code is 400 or 402 or 409 or 422 &&
                    (message.Contains("confirm_purchase", StringComparison.OrdinalIgnoreCase) ||
                     System.Text.RegularExpressions.Regex.IsMatch(message,
                     @"(?i)(licen[çc]|assinatur|subscription).*(insuficient|indispon|dispon[ií]ve|sufficient|available|contrat)|(?:insuficient|falt|not enough|no available).*(licen|assinatur|subscription)"));
                return new(code, success, json, needs,
                    success ? "Operação confirmada pela API." : needs ? "Não existem licenças disponíveis deste produto." :
                        fields?.ContainsKey("password")==true ? PasswordRules.ExplainRejection(message,code) : Explain(code),
                    code is 401 or 403 or 408 or 429 || code >= 500 || code is >= 300 and < 400);
            } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
                return new(null, false, default, false, "Resultado indeterminado. Confira o estado antes de repetir.", true);
            }
        }
    }
}
public sealed record ApiReply(int? Code, bool Success, JsonElement Root, bool NeedsLicense, string Message, bool Stop = false) {
    public JsonElement Data => JsonValue.Get(Root, "data");
}
public static class AdvancedPaths {
    public static bool IsAllowed(string method, string path) {
        var p = path.Split('?')[0];
        if (p.Contains("..") || p.Contains('\\') || p.StartsWith('/')) return false;
        if (method == "GET") return
            System.Text.RegularExpressions.Regex.IsMatch(p, @"^mailbox/[^/]+$|^mailbox/deleted/[^/]+$|^domain/[^/]+(?:/mail-products)?$|^client/[0-9]+/product(?:/[0-9]+)?$|^group/[^/]+$") ||
            new[] { "client", "report/messages/received", "report/messages/sent", "report/login" }.Contains(p);
        return method == "POST" && p == "group" ||
            method == "PUT" && System.Text.RegularExpressions.Regex.IsMatch(p, @"^group/[^/]+$") ||
            method == "PUT" && System.Text.RegularExpressions.Regex.IsMatch(p, @"^mailbox/[^/]+(?:/rename)?$");
    }
}
public static class JsonValue {
    public static JsonElement Get(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    public static string Text(JsonElement e, string name) {
        var v = Get(e, name);
        return v.ValueKind is JsonValueKind.String ? v.GetString() ?? "" :
            v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.ToString() : "";
    }
    public static decimal? Number(JsonElement e, string name) => decimal.TryParse(Text(e, name),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    public static JsonElement[] Array(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToArray() : System.Array.Empty<JsonElement>();
    public static string[] Strings(JsonElement e) => Array(e).Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray();
}
