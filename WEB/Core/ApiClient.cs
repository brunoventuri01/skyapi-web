using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkyAPI.Core;
public sealed record ApiResult(string Target,string Action,string Status,int? HttpStatus,string Message,bool StopBatch=false) { public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now; }
public sealed partial class ApiClient : IDisposable {
    public const string BaseUrl="https://api.skymail.net.br/v1/";
    /// <summary>
    /// A API Skymail recusa com HTTP 403 qualquer requisição sem User-Agent, inclusive o login.
    /// Este valor é o verificado contra a API real; não acompanhe a versão do aplicativo aqui
    /// sem antes confirmar que a API aceita a string nova.
    /// </summary>
    public const string UserAgent="SkyAPI/1.0";
    private readonly HttpClient http;
    private string token="";
    public bool HasToken=>token.Length>0;
    public ApiClient(HttpMessageHandler? handler=null){http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false});http.Timeout=TimeSpan.FromSeconds(60);http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);}
    public void SetToken(string value){Jwt.Validate(value.Trim());token=value.Trim();}
    public void ClearToken()=>token="";
    public async Task LoginAsync(string username,string password,string secret,CancellationToken ct=default){
        if(!Planner.IsEmail(username.Trim())||password.Length==0)throw new InvalidOperationException("Informe usuário e senha do painel.");
        byte[] key;
        try {key=Convert.FromBase64String(secret.Trim());if(key.Length==0)throw new FormatException();}
        catch(FormatException){throw new InvalidOperationException("A chave privada precisa estar no formato Base64 fornecido pelo painel.");}
        try {
            using var request=new HttpRequestMessage(HttpMethod.Post,BaseUrl+"auth/login");
            request.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"username",username.Trim()},{"password",password}});
            using var response=await SendControlledAsync(request,ct).ConfigureAwait(false);
            if(response.StatusCode!=HttpStatusCode.OK)throw new InvalidOperationException(ExplainLogin((int)response.StatusCode));
            using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            string? jti=null;
            if(data.RootElement.TryGetProperty("data",out var nested)&&nested.TryGetProperty("jti",out var id)&&id.ValueKind==JsonValueKind.String)jti=id.GetString();
            if(string.IsNullOrWhiteSpace(jti))throw new InvalidOperationException("A API não retornou o identificador de autenticação esperado. Consulte o suporte.");
            SetToken(Jwt.Create(jti,key));
        }catch(HttpRequestException){throw new InvalidOperationException("Não foi possível acessar a API. Verifique internet, proxy e firewall.");}
        catch(TaskCanceledException){throw new InvalidOperationException("A autenticação excedeu o tempo de espera ou foi cancelada.");}
        catch(JsonException){throw new InvalidOperationException("A API retornou uma resposta de autenticação inesperada.");}
        finally{CryptographicOperations.ZeroMemory(key);}
    }
    public async Task<ApiResult> ExecuteAsync(WorkItem item,CancellationToken ct=default){
        if(!HasToken)throw new InvalidOperationException("Configure a conexão antes de executar.");
        if(!item.Path.StartsWith("mailbox/",StringComparison.Ordinal)&&!item.Path.StartsWith("group/",StringComparison.Ordinal)&&!item.Path.StartsWith("dns/",StringComparison.Ordinal))throw new InvalidOperationException("Operação inválida.");
        using var request=new HttpRequestMessage(new HttpMethod(item.Method),BaseUrl+item.Path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        request.Headers.CacheControl=new CacheControlHeaderValue{NoCache=true};
        if(item.Fields.Count>0)request.Content=new FormUrlEncodedContent(item.Fields);
        try {
            using var response=await SendControlledAsync(request,ct).ConfigureAwait(false);
            int code=(int)response.StatusCode;
            if(code is 200 or 201 or 204)return new(item.Target,item.Description,"Sucesso",code,"Operação confirmada pela API.");
            if(code==202)return new(item.Target,item.Description,"Pendente",code,"Solicitação aceita. Confira a conclusão no painel antes de repetir.",true);
            return new(item.Target,item.Description,"Erro",code,Explain(code),code is 401 or 403 or 408 or 429 || code>=500 || code is >=300 and <400);
        }catch(TaskCanceledException){return new(item.Target,item.Description,"Indeterminado",null,"Tempo limite ou cancelamento. A alteração pode ter ocorrido. Confira no painel antes de repetir.",true);}
        catch(HttpRequestException){return new(item.Target,item.Description,"Indeterminado",null,"Falha de conexão. Confira no painel se a alteração ocorreu antes de repetir.",true);}
    }
    /// <summary>
    /// Mensagens do login. A chave privada não é enviada nesta etapa — ela só assina o token
    /// depois que o painel aceita usuário e senha —, então não faz sentido culpá-la aqui.
    /// </summary>
    public static string ExplainLogin(int code)=>code switch {
        400=>"O painel recusou os dados informados. Confira o usuário e a senha.",
        401=>"Usuário ou senha incorretos no painel Skymail.",
        403=>"O painel recusou o acesso deste usuário. Confira se ele tem permissão de administrador para a Interface API. A chave privada ainda não é usada nesta etapa.",
        404=>"Endereço de autenticação não encontrado. Confira se a Interface API está habilitada para a organização.",
        _=>Explain(code)
    };
    public static string Explain(int code)=>code switch {
        400=>"Dados rejeitados pela API. Confira os campos e as regras da conta.",401=>"Autenticação recusada. Gere ou informe um token válido.",
        403=>"Acesso negado. Verifique as permissões do usuário e a chave privada.",404=>"Registro não encontrado ou indisponível para esta operação.",
        409=>"Conflito com o estado atual. Verifique se o destino já existe.",422=>"A API rejeitou os dados. Confira os atributos e a política de senhas.",
        408=>"A API excedeu o tempo de espera. Confira o resultado no painel antes de repetir.",429=>"Limite de requisições atingido. Aguarde antes de iniciar outro lote.",
        >=500=>"A API apresentou uma falha. Confira o resultado no painel antes de repetir.",
        >=300 and <400=>"A API tentou redirecionar a solicitação. Por segurança, a execução foi interrompida.",
        _=>"Resposta inesperada da API (HTTP "+code+"). Consulte o suporte."
    };
    public void Dispose(){ClearToken();http.Dispose();}
}
public static class Jwt {
    private static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static byte[] Decode(string text){if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"^[A-Za-z0-9_-]+$"))throw new FormatException();return Convert.FromBase64String(text.Replace('-','+').Replace('_','/')+new string('=',(4-text.Length%4)%4));}
    public static string Create(string jti,byte[] key){
        var header=Encode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload=Encode(JsonSerializer.SerializeToUtf8Bytes(new{jti}));var data=header+"."+payload;
        return data+"."+Encode(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(data)));
    }
    public static void Validate(string token){
        try {
            if(token.Length>16000)throw new FormatException();var parts=token.Split('.');if(parts.Length!=3)throw new FormatException();
            using var head=JsonDocument.Parse(Decode(parts[0]));using var body=JsonDocument.Parse(Decode(parts[1]));
            if(!head.RootElement.TryGetProperty("alg",out var alg)||alg.GetString()!="HS256"||Decode(parts[2]).Length!=32)throw new FormatException();
            if(!body.RootElement.TryGetProperty("jti",out var jti)||jti.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(jti.GetString()))throw new FormatException();
            if(body.RootElement.TryGetProperty("exp",out var exp)&&(!exp.TryGetInt64(out var expires)||expires<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()))throw new FormatException();
        }catch(Exception ex) when(ex is FormatException or JsonException or InvalidOperationException){throw new InvalidOperationException("Token inválido ou expirado. Cole o JWT completo gerado para a API Skymail.");}
    }
}
