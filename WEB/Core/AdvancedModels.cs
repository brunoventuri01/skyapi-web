using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text;

namespace SkyAPI.Core;
public sealed record InputIssue(string Account, string Result, string Details);
public sealed record AccountInput(IReadOnlyList<string> Accounts, IReadOnlyList<InputIssue> Issues);
public sealed record GroupInput(string Name, string Email, string[] Members, string[] Writers, string[] Moderators) {
    public Dictionary<string,string> Fields() {
        var f = new Dictionary<string,string> { ["mail"] = Email, ["displayname"] = Name };
        void Add(string key, string[] values) { foreach (var v in values) f[key + "[" + v + "]"] = "true"; }
        Add("rfc822member", Members); Add("rfc822sender", Writers); Add("rfc822moderator", Moderators);
        return f;
    }
}
public sealed record GroupChange(string Role, string Address, bool Add);
public sealed record GroupEditPlan(string Email, IReadOnlyList<GroupChange> Changes) {
    // Uma única chamada por grupo: cada endereço vira uma chave própria, "true" inclui e vazio remove só ele.
    public Dictionary<string,string> Fields() {
        var f = new Dictionary<string,string>();
        foreach (var change in Changes) f[change.Role + "[" + change.Address + "]"] = change.Add ? "true" : "";
        return f;
    }
    private static string Label(string role) => role == "rfc822member" ? "membro" : role == "rfc822sender" ? "escritor" : "moderador";
    public IEnumerable<string> Lines() =>
        Changes.Select(c => (c.Add ? "adicionar " : "remover ") + Label(c.Role) + " " + c.Address +
            (c.Add ? " em " : " de ") + Email);
    public string Summary() => string.Join("; ", Changes.Select(c => (c.Add ? "+ " : "- ") + Label(c.Role) + " " + c.Address));
}
public static class BatchInput {
    // Brackets are control characters in the API's keyed form-array syntax.
    public static bool IsMailboxAddress(string value)=>Planner.IsEmail(value) && !value.Contains('[') && !value.Contains(']');
    public static List<string[]> Parse(string text) {
        if (text.Length > 5_000_000) throw new InvalidOperationException("Limite: 5 MB de texto.");
        text = text.TrimStart('\uFEFF');
        bool quoted = false; var counts = new Dictionary<char,int> { [',']=0, [';']=0, ['\t']=0 };
        for (int i=0;i<text.Length;i++) {
            if (text[i]=='"') { if(quoted && i+1<text.Length && text[i+1]=='"') {i++;continue;} quoted=!quoted; }
            if (!quoted && (text[i]=='\r' || text[i]=='\n')) break;
            if (!quoted && counts.ContainsKey(text[i])) counts[text[i]]++;
        }
        try {
            var rows = Csv.Parse(text, counts.OrderByDescending(x=>x.Value).First().Key).Where(x=>x.Any(v=>!string.IsNullOrWhiteSpace(v))).ToList();
            if (rows.Count>10001) throw new InvalidOperationException("Limite: 10.000 registros.");
            return rows;
        } catch(FormatException) { throw new InvalidOperationException("CSV inválido: confira aspas e separadores."); }
    }
    public static AccountInput Accounts(string text, string domain = "") {
        var rows=Parse(text); var accepted=new List<string>();var issues=new List<InputIssue>();
        if(rows.Count>0 && rows[0].Length==1 && new[]{"conta","email","e-mail","mailbox"}.Contains(rows[0][0].Trim().ToLowerInvariant()))rows.RemoveAt(0);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows) foreach(var cell in row) {
            var account=cell.Trim(); if(account.Length==0)continue;
            if(!IsMailboxAddress(account))issues.Add(new(account,"Inválida","E-mail inválido ou incompatível com o formato da API."));
            else if(domain.Length>0 && !account.Split('@')[1].Equals(domain,StringComparison.OrdinalIgnoreCase))issues.Add(new(account,"Inválida","Conta fora do domínio escolhido."));
            else if(!seen.Add(account))issues.Add(new(account,"Ignorada","Conta duplicada."));
            else accepted.Add(account);
        }
        if(accepted.Count>10000)throw new InvalidOperationException("Limite: 10.000 contas.");
        if(accepted.Count==0)throw new InvalidOperationException("Informe pelo menos uma conta válida.");
        return new(accepted,issues);
    }
    public static readonly string GroupEditHeader="grupo,acao,papel,endereco";
    public static (List<GroupEditPlan> Plans,List<InputIssue> Issues) GroupEdits(string text) {
        var rows=Parse(text); var issues=new List<InputIssue>();
        var expected=new[]{"grupo","acao","papel","endereco"};
        if(rows.Count==0 || !rows[0].Select(v=>Plain(v.Trim())).SequenceEqual(expected))
            throw new InvalidOperationException("Cabeçalho obrigatório: "+GroupEditHeader);
        var order=new List<string>();var byGroup=new Dictionary<string,List<GroupChange>>(StringComparer.OrdinalIgnoreCase);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in rows.Skip(1)) {
            var group=r.Length>0?r[0].Trim():"";
            if(r.Length!=4 || !IsMailboxAddress(group)) {issues.Add(new(group,"Inválida","Informe as quatro colunas e um grupo válido."));continue;}
            var action=Plain(r[1].Trim());var role=Plain(r[2].Trim());var address=r[3].Trim();
            bool add=action is "adicionar" or "incluir" or "add";
            if(!add && action is not ("remover" or "excluir" or "remove")) {issues.Add(new(group,"Inválida","Ação deve ser adicionar ou remover."));continue;}
            var field=role switch {
                "membro" or "membros" or "member"=>"rfc822member",
                "escritor" or "escritores" or "sender"=>"rfc822sender",
                "moderador" or "moderadores" or "moderator"=>"rfc822moderator",_=>""};
            if(field.Length==0) {issues.Add(new(group,"Inválida","Papel deve ser membro, escritor ou moderador."));continue;}
            if(!IsMailboxAddress(address)) {issues.Add(new(group,"Inválida","Endereço inválido: "+address));continue;}
            if(!seen.Add(group+"|"+field+"|"+address)) {issues.Add(new(group,"Ignorada","Alteração repetida para "+address+"."));continue;}
            if(!byGroup.TryGetValue(group,out var list)){list=new();byGroup[group]=list;order.Add(group);}
            list.Add(new(field,address,add));
        }
        var plans=order.Select(g=>new GroupEditPlan(g,byGroup[g])).ToList();
        if(plans.Count==0)throw new InvalidOperationException("Informe pelo menos uma alteração válida.");
        return(plans,issues);
    }
    // Cabeçalhos são comparados sem acento para aceitar "endereço" e "ação" digitados de qualquer jeito.
    private static string Plain(string value) {
        var text=value.Normalize(NormalizationForm.FormD);
        var builder=new StringBuilder();
        foreach(var c in text)if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark)builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
    public static (List<GroupInput> Groups,List<InputIssue> Issues) Groups(string text) {
        var rows=Parse(text); var groups=new List<GroupInput>();var issues=new List<InputIssue>();
        var expected=new[]{"grupo","email","membros","escritores","moderadores"};
        if(rows.Count==0 || !rows[0].Select(v=>Plain(v.Trim())).SequenceEqual(expected))
            throw new InvalidOperationException("Cabeçalho obrigatório: grupo,email,membros,escritores,moderadores");
        // Linhas repetidas com o mesmo e-mail somam pessoas ao grupo: da para escrever uma pessoa por linha,
        // sem depender do separador | dentro da celula.
        var order=new List<string>();
        var merged=new Dictionary<string,(string Name,List<string> Members,List<string> Writers,List<string> Moderators)>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in rows.Skip(1)) {
            var mail=r.Length>1?r[1].Trim():"";
            if(r.Length!=5 || !IsMailboxAddress(mail) || string.IsNullOrWhiteSpace(r[0])) {issues.Add(new(mail,"Inválida","Informe nome, e-mail e as cinco colunas."));continue;}
            string[] People(string value)=>value.Split(new[]{',',';','\t','\r','\n','|'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var members=People(r[2]);var writers=People(r[3]);var moderators=People(r[4]);
            if(members.Concat(writers).Concat(moderators).Any(v=>!IsMailboxAddress(v))){issues.Add(new(mail,"Inválida","Membro, escritor ou moderador inválido."));continue;}
            if(!merged.TryGetValue(mail,out var entry)) {
                entry=(r[0].Trim(),new List<string>(),new List<string>(),new List<string>());
                merged[mail]=entry;order.Add(mail);
            }
            entry.Members.AddRange(members);entry.Writers.AddRange(writers);entry.Moderators.AddRange(moderators);
        }
        string[] Unique(List<string> values)=>values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(var mail in order) {
            var entry=merged[mail];
            groups.Add(new(entry.Name,mail,Unique(entry.Members),Unique(entry.Writers),Unique(entry.Moderators)));
        }
        if(groups.Count==0)throw new InvalidOperationException("Informe pelo menos um grupo válido.");
        return(groups,issues);
    }
}
public sealed record MailProduct(string Name, string Id, string Type) {
    public decimal? CapacityBytes {
        get {
            var m=Regex.Match(Name,@"(?i)([0-9]+(?:[.,][0-9]+)?)\s*(GB|TB)\b");
            return m.Success && decimal.TryParse(m.Groups[1].Value.Replace(',','.'), NumberStyles.Float,CultureInfo.InvariantCulture,out var n)
                ? n * (m.Groups[2].Value.Equals("TB",StringComparison.OrdinalIgnoreCase)?1099511627776m:1073741824m) : null;
        }
    }
    // Live API types include "email", "exchange2013" and "exchange2013basic": match the family, not one value.
    public bool IsSkyMail=>Name.StartsWith("SkyMail",StringComparison.OrdinalIgnoreCase) && !Type.StartsWith("exchange",StringComparison.OrdinalIgnoreCase);
    public bool IsSkyExchange=>LicenseRules.IsExchange(Name) && (Type.Length==0 || Type.StartsWith("exchange",StringComparison.OrdinalIgnoreCase));
}
public sealed record MailboxInfo(string Account,string Product,decimal? Quota,decimal? Used,string[] Aliases,string Groups,bool Processing) {
    public decimal? Percent=>Quota>0 && Used>=0 ? Used/Quota*100m : null;
    public static MailboxInfo Parse(JsonElement d, string expected) {
        var mail=JsonValue.Text(d,"mail");
        // Confirmed against the live API on 09/09/2026: "accounttype" is a short label that does NOT match the
        // product catalogue ("SkyMail 500GB" for "SkyMail Premium 500GB", "Exchange 50GB" for "SkyExchange
        // 50GB"). Only "productname" carries the catalogue name, which is what licence rules and the
        // post-validation of a change compare against. "accounttype" stays as fallback.
        var product=JsonValue.Text(d,"productname");
        if(product.Length==0)product=JsonValue.Text(d,"accounttype");
        if(!mail.Equals(expected,StringComparison.OrdinalIgnoreCase)||product.Length==0)throw new InvalidOperationException("A API não retornou a caixa e o produto esperados.");
        var q=JsonValue.Get(d,"mailBoxQuotaSize"); var g=JsonValue.Get(d,"groups");
        return new(mail,product,JsonValue.Number(q,"mailquotasize"),JsonValue.Number(q,"used"),
            JsonValue.Strings(JsonValue.Get(d,"mailalternateaddress")),
            g.ValueKind==JsonValueKind.Object?string.Join("; ",g.EnumerateObject().SelectMany(p=>JsonValue.Strings(p.Value).Select(v=>p.Name+": "+v))):"",
            JsonValue.Text(d,"renameprocessing").Equals("True",StringComparison.OrdinalIgnoreCase));
    }
}
public sealed record LicenseCheck(string Account,string Current,string Destination,string Result,string Details) {
    public bool Eligible=>Result=="Pronta";
}
// Linha SkyMail publicada pela Skymail. O catálogo do domínio traz só o que o cliente já contratou no
// painel, então uma caixa cheia ficava sem sugestão nenhuma quando o tamanho maior existe na Skymail mas ainda
// não está no cadastro daquele cliente. Aqui fica o que existe para contratar; o produto ainda precisa ser
// adicionado no painel antes de a alteração em lote conseguir usá-lo.
public static class SkyMailLine {
    public static readonly IReadOnlyList<MailProduct> Products=new[] {
        new MailProduct("SkyMail 5GB","",""),new MailProduct("SkyMail 25GB","",""),
        new MailProduct("SkyMail Premium 50GB","",""),new MailProduct("SkyMail Premium 100GB","",""),
        new MailProduct("SkyMail Premium 200GB","",""),new MailProduct("SkyMail Premium 500GB","","")
    };
}
/// <summary>Upgrade sugerido para uma caixa. InPanel diz se o produto já está contratado no painel do cliente:
/// falso significa sugestão válida que só pode ser enviada depois de adicionar o produto por lá.</summary>
public sealed record UpgradePlan(MailProduct Product,bool InPanel);
public static class SkyExchangeLine {
    public static readonly IReadOnlyList<MailProduct> Products=new[] {
        new MailProduct("SkyExchange Basic 5GB","",""),new MailProduct("SkyExchange Basic 25GB","",""),
        new MailProduct("SkyExchange 50GB","",""),new MailProduct("SkyExchange 100GB","",""),
        new MailProduct("SkyExchange 200GB","","")
    };
}
public static class LicenseRules {
    public const string ExchangeBlocked="Conversões de SkyExchange para SkyMail devem ser realizadas pela equipe Skymail mediante abertura de chamado.";
    public static bool IsExchange(string name)=>name.StartsWith("SkyExchange",StringComparison.OrdinalIgnoreCase) || name.StartsWith("Exchange ",StringComparison.OrdinalIgnoreCase);
    private static bool Compatible(MailboxInfo box,MailProduct product)=>
        IsExchange(box.Product)?product.IsSkyExchange:box.Product.StartsWith("SkyMail",StringComparison.OrdinalIgnoreCase)&&product.IsSkyMail;
    public static LicenseCheck Check(MailboxInfo box, MailProduct destination) {
        if(box.Product.Equals(destination.Name,StringComparison.Ordinal)) return new(box.Account,box.Product,destination.Name,"Ignorada","Sem alteração. A caixa já utiliza "+destination.Name+".");
        if(IsExchange(box.Product)&&destination.IsSkyMail)return new(box.Account,box.Product,destination.Name,"Bloqueada",ExchangeBlocked);
        if(!Compatible(box,destination))return new(box.Account,box.Product,destination.Name,"Bloqueada","A alteração deve permanecer na mesma família: SkyMail → SkyMail ou SkyExchange → SkyExchange. Conversões entre famílias exigem atendimento da Skymail.");
        if(box.Processing)return new(box.Account,box.Product,destination.Name,"Pendente","Renomeação em processamento.");
        return new(box.Account,box.Product,destination.Name,"Pronta",box.Product+" → "+destination.Name);
    }
    public static MailProduct? Suggest(MailboxInfo box,IEnumerable<MailProduct> products) =>
        box.Quota>0 && box.Used>=0
        ? products.Where(p=>Compatible(box,p) && p.CapacityBytes>box.Quota && p.CapacityBytes>box.Used && p.Name!=box.Product)
            .OrderBy(p=>p.CapacityBytes).FirstOrDefault() : null;
    /// <summary>Menor produto da mesma família que comporta a caixa: primeiro no catálogo do domínio, que é o que dá para
    /// enviar hoje; não havendo nenhum lá, na linha publicada da Skymail. Nesse segundo caso a sugestão
    /// continua valendo — só depende de o cliente adicionar o produto no painel antes.</summary>
    public static UpgradePlan? Plan(MailboxInfo box,IEnumerable<MailProduct> catalog) {
        var contracted=Suggest(box,catalog);
        if(contracted!=null)return new(contracted,true);
        var published=Suggest(box,IsExchange(box.Product)?SkyExchangeLine.Products:SkyMailLine.Products);
        return published==null?null:new(published,false);
    }
}
public static class PasswordRules {
    public const string Requirements="Use pelo menos 8 caracteres e combine pelo menos 3 tipos: letras maiúsculas, letras minúsculas, números e símbolos.";
    public static int TypeCount(string password)=>(password.Any(char.IsUpper)?1:0)+(password.Any(char.IsLower)?1:0)+
        (password.Any(char.IsDigit)?1:0)+(password.Any(c=>!char.IsLetterOrDigit(c)&&!char.IsWhiteSpace(c))?1:0);
    public static string? ReplacementError(string password,params string[] accounts) {
        int types=TypeCount(password);
        if(password.Length<8 || types<3)return "A nova senha é muito fraca. "+Requirements;
        var normalized=Normalize(password);
        foreach(var sequence in new[]{"01234567890","abcdefghijklmnopqrstuvwxyz","qwertyuiop","asdfghjkl","zxcvbnm"}) {
            var reverse=new string(sequence.Reverse().ToArray());
            for(int i=0;i<=normalized.Length-3;i++) {
                var part=normalized.Substring(i,3);
                if(sequence.Contains(part,StringComparison.Ordinal)||reverse.Contains(part,StringComparison.Ordinal))
                    return "Não use sequências de 3 ou mais números ou letras, em ordem direta, inversa ou no teclado.";
            }
        }
        foreach(var account in accounts) {
            // Compare meaningful components, including names separated by punctuation or numbers.
            var terms=Regex.Matches(Normalize(account),@"[a-z0-9]+|[^\W\d_]+").Cast<Match>().Select(m=>m.Value)
                .Concat(Regex.Matches(Normalize(account),@"[a-z]+").Cast<Match>().Select(m=>m.Value));
            if(terms.Any(term=>term.Length>=3 && normalized.Contains(term,StringComparison.Ordinal)))
                return "A senha não pode conter nomes ou partes da conta atual, da nova conta ou dos domínios (a partir de 3 caracteres).";
        }
        return null;
    }
    private static string Normalize(string value)=>new string(value.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
    public static string Generate(params string[] accounts) {
        const string alphabet="ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%&*+-?";
        for(int attempt=0;attempt<1000;attempt++) {
            var chars=new char[10];
            for(int i=0;i<chars.Length;i++)chars[i]=alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
            var candidate=new string(chars);
            if(ReplacementError(candidate,accounts)==null)return candidate;
        }
        throw new InvalidOperationException("Não foi possível gerar uma senha válida. Tente novamente.");
    }
    public static string ExplainRejection(string message,int code) {
        if(message.Contains("Very weak password",StringComparison.OrdinalIgnoreCase))return "A API recusou a senha por ser muito fraca. "+Requirements;
        if(message.Contains("diferente da atual",StringComparison.OrdinalIgnoreCase))return "A API recusou a senha porque ela é igual à senha atual. Informe uma senha diferente.";
        return ApiClient.Explain(code);
    }
}
public sealed record OperationRow(string Account,string Result,string Details) {
    public string Product {get;init;}="";
    public string Suggested {get;init;}="";
    public string Usage {get;init;}="";
    public string Quota {get;init;}="";
    public string Aliases {get;init;}="";
    /// <summary>Falso quando o produto sugerido existe na Skymail mas ainda não está no painel do cliente.</summary>
    public bool SuggestionInPanel {get;init;}=true;
    public string Groups {get;init;}="";
}
public sealed record MessageRow(string Date,string Sender,string Recipient,string Subject,string Size,string Status);
// Formatação de exibição. Fica no núcleo para ser testada sem abrir a interface.
public static class Display {
    // Vírgula decimal sem depender de dados de cultura instalados: em modo globalização invariante,
    // pedir "pt-BR" lança exceção, e isso derrubaria a tela em vez de formatar um número.
    private static readonly CultureInfo Br=Brazilian();
    private static CultureInfo Brazilian() {
        try {return CultureInfo.GetCultureInfo("pt-BR");}
        catch(CultureNotFoundException) {
            var fallback=(CultureInfo)CultureInfo.InvariantCulture.Clone();
            fallback.NumberFormat.NumberDecimalSeparator=",";
            fallback.NumberFormat.NumberGroupSeparator=".";
            return fallback;
        }
    }
    /// <summary>Bytes em KB ou MB. Abaixo de 1 MB, MB viraria "0,01 MB" e não se lê.</summary>
    public static string Size(string raw) {
        if(!decimal.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var bytes)||bytes<0)return "";
        if(bytes<1024)return bytes.ToString("0",Br)+" bytes";
        var kb=bytes/1024m;
        return kb<1024m?kb.ToString("0.#",Br)+" KB":(kb/1024m).ToString("0.##",Br)+" MB";
    }
    public static string Percent(decimal value)=>value.ToString("0.#",Br)+"%";
    /// <summary>Uso da caixa: GB e, quando a quota é conhecida, o percentual. O sinal de porcentagem sai de
    /// Percent, uma vez só — juntar os dois na tela já duplicou o sinal.</summary>
    public static string Usage(decimal? bytes,decimal? percent)=>
        Gigabytes(bytes)+(percent!=null?" · "+Percent(percent.Value):"");
    public static string Gigabytes(decimal? bytes)=>
        bytes is null or <0?"":(bytes.Value/1073741824m).ToString(bytes.Value>=107374182m?"0.##":"0.###",Br)+" GB";
    /// <summary>"2026-10-31T15:45:00-03:00" vira "31/10/2026 às 15:45". Sem data reconhecida, devolve como veio.</summary>
    public static string When(string raw) {
        if(raw.Length==0)return "";
        return DateTimeOffset.TryParse(raw,Br,DateTimeStyles.None,out var moment)||
               DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.None,out moment)
            ? moment.ToLocalTime().ToString("dd/MM/yyyy",Br)+" às "+moment.ToLocalTime().ToString("HH:mm",Br)
            : raw;
    }
    // A API devolve assuntos já corrompidos: texto UTF-8 que em algum ponto foi lido como Latin-1 e regravado.
    // Aqui isso é desfeito, mas só quando a releitura forma UTF-8 válido — caso contrário o texto fica intacto.
    public static string Repair(string text) {
        if(text.Length==0||!text.Any(c=>c>=0x80&&c<=0xFF)||text.Any(c=>c>0xFF))return text;
        try {
            var bytes=Encoding.Latin1.GetBytes(text);
            var strict=new UTF8Encoding(false,true);
            var candidate=strict.GetString(bytes);
            return candidate.Any(c=>c is >= (char)0x80 and <= (char)0x9F)?text:candidate;
        } catch(DecoderFallbackException){return text;}
    }
}
public sealed record LoginRow(string Date,string Account,string Ip,string Protocol,string Status);
// Report rows repeat themselves inside a "message" string as embedded JSON, and some fields exist ONLY there.
public static class ApiRow {
    public static string Text(JsonElement e,params string[] names) {
        foreach(var name in names){var value=JsonValue.Text(e,name);if(value.Length>0)return value;}
        return "";
    }
    public static JsonElement Embedded(JsonElement row) {
        var raw=JsonValue.Text(row,"message");
        if(raw.Length==0)return default;
        try {using var doc=JsonDocument.Parse(raw);return doc.RootElement.Clone();}
        catch(JsonException){return default;}
    }
}
// Field names confirmed against the live API on 09/09/2026: the row carries User, @timestamp, Motivo and a
// "message" string with the record repeated as JSON, where RemoteIP and Protocolo are the only source of IP
// and protocol. Lower-case variants are accepted so a change of casing does not empty the report silently.
public static class LoginRecord {
    public static string Account(JsonElement row)=>ApiRow.Text(row,"User","user");
    public static LoginRow Parse(JsonElement row,string account) {
        var detail=ApiRow.Embedded(row);
        return new(ApiRow.Text(row,"@timestamp","timestamp","date"),account,
            ApiRow.Text(detail,"RemoteIP","remoteip","ip"),ApiRow.Text(detail,"Protocolo","protocolo","protocol"),
            ApiRow.Text(row,"Motivo","motivo","status"));
    }
}
// Also confirmed live on 09/09/2026. Received rows bring @timestamp and keep "Tamanho" only inside the
// embedded JSON; sent rows have no @timestamp at all (DataEnvio and timestamp instead) and carry no size.
// Reading only the top level left the date of every sent message and every size empty.
public static class MessageRecord {
    public static MessageRow Parse(JsonElement row) {
        var detail=ApiRow.Embedded(row);
        var subject=ApiRow.Text(row,"Assunto");
        if(subject.Length==0)subject=ApiRow.Text(detail,"Assunto");
        var size=ApiRow.Text(row,"Tamanho");
        if(size.Length==0)size=ApiRow.Text(detail,"Tamanho");
        // Enviadas trazem a resposta do SMTP em Motivo; recebidas não têm Motivo, e sim a pasta de entrega.
        var status=ApiRow.Text(row,"Motivo");
        if(status.Length==0)status=ApiRow.Text(detail,"Motivo");
        if(status.Length==0)status=ApiRow.Text(row,"MailBox","mailbox");
        if(status.Length==0)status=ApiRow.Text(detail,"MailBox","mailbox");
        return new(Display.When(ApiRow.Text(row,"@timestamp","DataEnvio","timestamp")),
            ApiRow.Text(row,"De"),ApiRow.Text(row,"Para"),
            Display.Repair(subject.Trim()),Display.Size(size),Display.Repair(status));
    }
}
public static class Exports {
    public static string FileName(string module)=>"SkyAPI_"+Regex.Replace(module,@"[^\p{L}0-9_]","_")+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".csv";
    public static string CsvText(string[] headers,IEnumerable<string[]> rows)=>string.Join(";",headers.Select(Csv.Cell))+"\r\n"+
        string.Join("\r\n",rows.Select(r=>string.Join(";",r.Select(v=>Csv.Cell(v??"")))));
}
public sealed class OperationLog : IDisposable {
    private readonly StreamWriter writer;
    public OperationLog(string directory,string module) {
        Directory.CreateDirectory(directory);
        writer=new StreamWriter(new FileStream(Path.Combine(directory,Path.GetFileNameWithoutExtension(Exports.FileName(module))+"_"+Guid.NewGuid().ToString("N")[..6]+".json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false)){AutoFlush=true};
        writer.Write("["); first=true;
    }
    private bool first;
    public void Write(string module,string account,string operation,string result,TimeSpan elapsed) {
        if(!first)writer.Write(",");first=false;
        // Strict allowlist: no response body, request fields, free-form details or credentials.
        writer.Write(JsonSerializer.Serialize(new{data=DateTimeOffset.Now,modulo=module,conta=account,
            dominio=account.Contains('@')?account.Split('@')[1]:"",operacao=operation,resultado=result,tempo=elapsed.TotalSeconds}));
    }
    public void Dispose(){writer.Write("]");writer.Dispose();}
}
