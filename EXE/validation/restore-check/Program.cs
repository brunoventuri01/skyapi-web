// Diagnostico do erro "Registro nao encontrado ou indisponivel para esta operacao" na recuperacao
// de contas. Pergunta a API, conta a conta, se ela reconhece a caixa como excluida — que e
// exatamente a primeira coisa que os dois aplicativos fazem antes de restaurar.
//
// Somente GET. Nada e alterado, nada e restaurado.
//
// As credenciais sao lidas da entrada padrao e apagadas da memoria logo apos o login: nao ficam
// em arquivo, em variavel de ambiente, nem em linha de comando (onde apareceriam no historico).
//
// Uso:
//   dotnet run --project EXE/validation/restore-check
// e responda as perguntas na tela.

using SkyAPI.Core;
using System.Text.Json;

static string Ask(string label, bool secret = false) {
    Console.Write(label);
    if (!secret) return Console.ReadLine() ?? "";
    var typed = new System.Text.StringBuilder();
    while (true) {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return typed.ToString(); }
        if (key.Key == ConsoleKey.Backspace) { if (typed.Length > 0) typed.Length--; continue; }
        if (!char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
    }
}

Console.WriteLine("Diagnostico de recuperacao de contas — somente leitura.");
Console.WriteLine();

var username = Ask("Usuario do painel: ");
var password = Ask("Senha do painel: ", secret: true);
var key = Ask("Chave privada da API: ", secret: true);

using var api = new ApiClient();
try {
    await api.LoginAsync(username, password, key);
} finally {
    username = password = key = "";   // fora da memoria antes de qualquer outra chamada
}
Console.WriteLine("Autenticado.");
Console.WriteLine();

Console.WriteLine("Cole as contas que voce tentou recuperar, uma por linha.");
Console.WriteLine("Termine com uma linha vazia.");
var accounts = new List<string>();
while (Console.ReadLine() is string line && line.Trim().Length > 0) accounts.Add(line.Trim());
Console.WriteLine();

if (accounts.Count == 0) { Console.WriteLine("Nenhuma conta informada."); return 1; }

int found = 0, missing = 0, other = 0;
foreach (var account in accounts) {
    var reply = await api.RequestAsync("GET", "mailbox/deleted/" + Uri.EscapeDataString(account));
    if (reply.Success) {
        found++;
        var client = JsonValue.Text(reply.Data, "clientId");
        var product = JsonValue.Text(reply.Data, "productname");
        if (product.Length == 0) product = JsonValue.Text(reply.Data, "accounttype");
        var deleted = JsonValue.Text(reply.Data, "deletedDate");
        Console.WriteLine($"[{reply.Code}] {account} — reconhecida como excluida. " +
            $"cliente={(client.Length > 0 ? client : "(ausente)")} produto={(product.Length > 0 ? product : "(ausente)")} " +
            $"excluida em={(deleted.Length > 0 ? deleted : "(nao informado)")}");
        // O aplicativo exige o clientId numerico para saber de qual saldo tirar a licenca.
        if (!long.TryParse(client, out _))
            Console.WriteLine($"    ATENCAO: a API nao devolveu clientId numerico para {account}; a conferencia pararia aqui.");
    } else if (reply.Code == 404) {
        missing++;
        Console.WriteLine($"[404] {account} — a API NAO tem esta conta entre as caixas excluidas.");
    } else {
        other++;
        Console.WriteLine($"[{reply.Code?.ToString() ?? "sem resposta"}] {account} — {reply.Message}");
    }
}

api.ClearToken();

Console.WriteLine();
Console.WriteLine($"Resumo: {found} reconhecida(s) como excluida(s), {missing} com 404, {other} com outra resposta.");
Console.WriteLine();
if (missing > 0) {
    Console.WriteLine("As contas com 404 sao a causa do erro: basta UMA delas para a conferencia inteira parar,");
    Console.WriteLine("porque o aplicativo confere licenca de todas antes de restaurar qualquer uma.");
    Console.WriteLine("Verifique, para essas contas: se foram mesmo excluidas, se ja nao foram restauradas,");
    Console.WriteLine("e se a exclusao terminou de processar (tente de novo depois de alguns minutos).");
}
if (found > 0 && missing == 0 && other == 0)
    Console.WriteLine("Todas foram reconhecidas: o erro esta em outro ponto. Rode de novo assim que ele acontecer.");

return 0;
