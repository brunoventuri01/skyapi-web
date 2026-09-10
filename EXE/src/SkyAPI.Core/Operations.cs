using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace SkyAPI.Core;
public enum Operation { DeleteAccounts, RestoreAccounts, AccountStatus, PasswordSame, PasswordDifferent, ForcePasswordChange, RenameAccounts, Attributes, DeleteGroups, DeleteDns }
public sealed record WorkItem(string Target, string Method, string Path, IReadOnlyDictionary<string,string> Fields, string Description);
public sealed record Plan(IReadOnlyList<WorkItem> Items, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public static class Planner {
    public static readonly IReadOnlyDictionary<string,string> AttributeMap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        ["mailbox"]="mailbox",["Nome"]="displayname",["Email secundario"]="secondarymail",["Email secundário"]="secondarymail",
        ["Celular"]="mobilephone",["Telefone residencial"]="homephone",["Telefone comercial"]="companyphone",["Empresa"]="company",
        ["Unidade"]="physicaldeliveryofficename",["Departamento"]="department",["Cargo"]="title",["Ramal"]="companyphoneextension"
    };
    public static bool IsEmail(string value) => value.Length<=254 && !value.Any(char.IsWhiteSpace) && MailAddress.TryCreate(value,out var m) && m.Address==value && value.Contains('@') && value[(value.LastIndexOf('@')+1)..].Contains('.') && !value.Contains('/') && !value.Contains('\\');
    public static bool IsDomain(string value) => value.Length<=253 && Regex.IsMatch(value,@"^(?=.{1,253}$)(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$");
    public static string Template(Operation op) => op switch {
        Operation.PasswordDifferent=>"conta1@exemplo.com.br;SenhaExemplo@123\r\nconta2@exemplo.com.br;OutraSenha@456",
        Operation.RenameAccounts=>"conta1@exemplo.com.br,conta1.nova@exemplo.com.br\r\nconta2@exemplo.com.br,conta2.nova@exemplo.com.br",
        Operation.Attributes=>"mailbox,Nome,Cargo\r\nconta1@exemplo.com.br,Ana Silva,Gerente\r\nconta2@exemplo.com.br,Bruno Costa,Analista",
        Operation.DeleteDns=>"exemplo.com.br\r\noutroexemplo.com.br",
        Operation.DeleteGroups=>"grupo1@exemplo.com.br\r\ngrupo2@exemplo.com.br",
        _=>"conta1@exemplo.com.br\r\nconta2@exemplo.com.br"
    };
    public static Plan Build(Operation op,string input,string status="disabled",string password="") {
        var items=new List<WorkItem>(); var errors=new List<string>();var warnings=new List<string>();
        if(input.Length>5_000_000) return new(items,new[]{"Arquivo muito grande. Limite: 5 MB de texto."},warnings);
        if(op==Operation.AccountStatus && !new[]{"disabled","noaccess","active"}.Contains(status)) return new(items,new[]{"Escolha um status válido."},warnings);
        if(op==Operation.PasswordSame && string.IsNullOrEmpty(password)) return new(items,new[]{"Informe a nova senha."},warnings);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var renamed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records=new List<string[]>();
        try {
            if(op is Operation.Attributes or Operation.RenameAccounts) {
                var first=input.Split('\n')[0];char sep=first.Count(c=>c==';')>first.Count(c=>c==',')?';':',';
                records=Csv.Parse(input.TrimStart('\uFEFF'),sep);
            } else {
                foreach(var line in input.TrimStart('\uFEFF').Replace("\r\n","\n").Replace('\r','\n').Split('\n')) {
                    if(string.IsNullOrWhiteSpace(line))continue;
                    if(op==Operation.PasswordDifferent){var pos=line.IndexOf(';');records.Add(pos<0?new[]{line}:new[]{line[..pos],line[(pos+1)..]});}
                    else records.Add(new[]{line.Trim()});
                }
            }
        } catch(FormatException){errors.Add("CSV inválido: verifique aspas, separadores e linhas incompletas.");return new(items,errors,warnings);}
        if(records.Count>10001)return new(items,new[]{"Limite de 10.000 registros por lote."},warnings);
        string[] headers=Array.Empty<string>();
        if(op==Operation.Attributes && records.Count>0){
            headers=records[0].Select(s=>s.Trim()).Select(s=>AttributeMap.TryGetValue(s,out var v)?v:s.ToLowerInvariant()).ToArray();records.RemoveAt(0);
            var allowed=AttributeMap.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(!headers.Contains("mailbox"))errors.Add("A coluna mailbox é obrigatória.");
            if(headers.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=headers.Length)errors.Add("Existem colunas repetidas no cabeçalho.");
            if(headers.Any(h=>!allowed.Contains(h)))errors.Add("Há colunas desconhecidas. Use apenas os campos do modelo e da ajuda.");
            if(errors.Count>0)return new(items,errors,warnings);
        }
        int n=op==Operation.Attributes?1:0;
        foreach(var record in records){
            n++;if(record.All(string.IsNullOrWhiteSpace))continue;
            var line="Linha "+n+": ";
            if((op is Operation.RenameAccounts or Operation.PasswordDifferent)&&record.Length!=2){errors.Add(line+"informe duas colunas no formato indicado.");continue;}
            if(op==Operation.Attributes&&record.Length!=headers.Length){errors.Add(line+"quantidade de colunas diferente do cabeçalho.");continue;}
            var target=(op==Operation.Attributes?record[Array.IndexOf(headers,"mailbox")]:record[0]).Trim();
            if(!(op==Operation.DeleteDns?IsDomain(target):IsEmail(target))){errors.Add(line+(op==Operation.DeleteDns?"domínio inválido.":"e-mail inválido."));continue;}
            if(!seen.Add(target)){errors.Add(line+"item duplicado: "+target);continue;}
            var fields=new Dictionary<string,string>();string method="PUT",path="mailbox/"+Uri.EscapeDataString(target),description="";
            switch(op){
                case Operation.DeleteAccounts: method="DELETE";description="Excluir conta";break;
                case Operation.RestoreAccounts:path="mailbox/deleted/"+Uri.EscapeDataString(target)+"/restore";description="Restaurar conta";break;
                case Operation.AccountStatus:fields["accountstatus"]=status;description=status switch{"disabled"=>"Desabilitar conta","noaccess"=>"Bloquear acesso",_=>"Reativar conta"};break;
                case Operation.PasswordSame:fields["password"]=password;description="Alterar senha (oculta)";break;
                case Operation.PasswordDifferent:
                    if(string.IsNullOrEmpty(record[1])){errors.Add(line+"senha vazia.");continue;}
                    fields["password"]=record[1];description="Alterar senha individual (oculta)";break;
                case Operation.ForcePasswordChange:fields["forcepasswordchange"]="true";description="Exigir troca no próximo login";break;
                case Operation.RenameAccounts:
                    var next=record[1].Trim();if(!IsEmail(next)){errors.Add(line+"novo e-mail inválido.");continue;}
                    if(string.Equals(next,target,StringComparison.OrdinalIgnoreCase)){errors.Add(line+"o novo e-mail é igual ao atual.");continue;}
                    if(!renamed.Add(next)){errors.Add(line+"o novo e-mail já aparece como destino no lote.");continue;}
                    path+="/rename";fields["mail"]=next;description="Renomear para "+next;break;
                case Operation.Attributes:
                    for(int i=0;i<headers.Length;i++){
                        var field=headers[i];var value=record[i].Trim();if(field=="mailbox"||value.Length==0)continue;
                        if(field=="secondarymail"&&!IsEmail(value)){errors.Add(line+"e-mail secundário inválido.");continue;}
                        if(new[]{"mobilephone","homephone","companyphone"}.Contains(field)){
                            var digits=Regex.Replace(value,@"\D","");
                            if(digits.Length is not (10 or 11)){errors.Add(line+"telefone deve ter 10 ou 11 dígitos (DDD + número), sem +55.");continue;}
                            value=digits[..2]+" "+digits.Substring(2,4)+"-"+digits[6..];
                        }
                        fields[field]=value;
                    }
                    if(fields.Count==0){errors.Add(line+"nenhum atributo preenchido para atualizar.");continue;}
                    description="Atualizar: "+string.Join(", ",fields.Keys);break;
                case Operation.DeleteGroups:method="DELETE";path="group/"+Uri.EscapeDataString(target);description="Excluir grupo permanentemente";break;
                case Operation.DeleteDns:method="DELETE";path="dns/"+Uri.EscapeDataString(target);description="Excluir zona DNS permanentemente";break;
            }
            items.Add(new(target,method,path,fields,description));
        }
        if(op==Operation.RenameAccounts&&items.Any(x=>seen.Contains(x.Fields["mail"])))errors.Add("Um destino de renomeação também é origem neste lote. Separe as alterações para evitar conflitos.");
        if(items.Count==0&&errors.Count==0)errors.Add("Informe pelo menos um registro.");
        if(op==Operation.Attributes)warnings.Add("Campos vazios serão mantidos como estão. Telefones inválidos bloqueiam a conferência para evitar alterações parciais.");
        return new(items,errors,warnings);
    }
}
public static class Csv {
    public static List<string[]> Parse(string text,char sep=',') {
        var rows=new List<string[]>();var row=new List<string>();var field=new StringBuilder();bool quoted=false,afterQuote=false;
        for(int i=0;i<text.Length;i++){
            char c=text[i];
            if(quoted){if(c=='"'){if(i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else {quoted=false;afterQuote=true;}}else field.Append(c);continue;}
            if(c=='"'){if(field.Length!=0||afterQuote)throw new FormatException();quoted=true;continue;}
            if(c==sep){row.Add(field.ToString());field.Clear();afterQuote=false;continue;}
            if(c=='\r'||c=='\n'){if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;row.Add(field.ToString());rows.Add(row.ToArray());row.Clear();field.Clear();afterQuote=false;continue;}
            if(afterQuote)throw new FormatException();field.Append(c);
        }
        if(quoted)throw new FormatException();
        if(field.Length>0||row.Count>0||afterQuote){row.Add(field.ToString());rows.Add(row.ToArray());}
        return rows;
    }
    public static string Cell(string value){if(value.Length>0&&"=+-@\t\r".Contains(value[0]))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
}
