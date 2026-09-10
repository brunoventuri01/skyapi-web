using System.Net.Http.Headers;
using SkyAPI.Core;
namespace SkyAPI.Web;

/// <summary>
/// O núcleo continua endereçando a Skymail. Só este adaptador de navegador reescreve a chamada
/// para o encaminhador de mesma origem em /api/skymail, que repõe User-Agent e Cache-Control —
/// cabeçalhos que o navegador proíbe a página de definir.
/// O corpo viaja como octet-stream com o tipo real em X-Sky-Content-Type para que nenhum
/// intermediário reinterprete os campos do formulário.
/// </summary>
public sealed class BrowserTransport:HttpMessageHandler {
    public const string ContentTypeHeader="X-Sky-Content-Type";
    private readonly HttpClient browser;
    public BrowserTransport(string origin){browser=new(){BaseAddress=new Uri(origin),Timeout=TimeSpan.FromSeconds(65)};}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
        var address=request.RequestUri!.AbsoluteUri;
        if(!address.StartsWith(ApiClient.BaseUrl,StringComparison.Ordinal))throw new InvalidOperationException("Destino inválido.");
        var path=address[ApiClient.BaseUrl.Length..];
        var forwarded=new HttpRequestMessage(request.Method,"api/skymail?path="+Uri.EscapeDataString(path));
        forwarded.Headers.Authorization=request.Headers.Authorization;
        if(request.Content!=null) {
            var body=await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var type=request.Content.Headers.ContentType?.ToString();
            forwarded.Content=new ByteArrayContent(body);
            forwarded.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");
            if(!string.IsNullOrEmpty(type))forwarded.Headers.TryAddWithoutValidation(ContentTypeHeader,type);
        }
        // A resposta é devolvida inteira ao núcleo, com os cabeçalhos de limite preservados.
        return await browser.SendAsync(forwarded,HttpCompletionOption.ResponseContentRead,ct).ConfigureAwait(false);
    }
    protected override void Dispose(bool disposing){if(disposing)browser.Dispose();base.Dispose(disposing);}
}
