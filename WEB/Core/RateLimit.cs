using System.Net.Http;
using System.Threading;
namespace SkyAPI.Core;
public sealed partial class ApiClient {
    private readonly SemaphoreSlim requestGate=new(1,1);
    private DateTimeOffset nextRequest,blockedUntil;
    public event Action<DateTimeOffset?>? RateLimitWaiting;
    private async Task<HttpResponseMessage> SendControlledAsync(HttpRequestMessage request,CancellationToken ct) {
        await requestGate.WaitAsync(ct).ConfigureAwait(false);
        try {
            var now=DateTimeOffset.UtcNow;
            var until=nextRequest>blockedUntil?nextRequest:blockedUntil;
            if(until>now) {
                if(blockedUntil>now)RateLimitWaiting?.Invoke(until);
                try {await Task.Delay(until-now,ct).ConfigureAwait(false);}
                finally {RateLimitWaiting?.Invoke(null);}
            }
            nextRequest=DateTimeOffset.UtcNow.AddMilliseconds(550);
            var response=await http.SendAsync(request,ct).ConfigureAwait(false);
            long? Header(string name)=>response.Headers.TryGetValues(name,out var values) && long.TryParse(values.FirstOrDefault(),out var n)?n:null;
            var remaining=Header("X-RateLimit-Remaining");
            var reset=Header("X-RateLimit-Reset");
            var limit=Header("X-RateLimit-Limit");
            if(limit is >0 and <120)nextRequest=DateTimeOffset.UtcNow.AddMilliseconds(66000d/limit.Value);
            if(remaining==0 || (int)response.StatusCode==429) {
                var resume=DateTimeOffset.UtcNow.AddSeconds(60);
                if(reset is >0 and <253402300799) {
                    var reported=DateTimeOffset.FromUnixTimeSeconds(reset.Value).AddSeconds(1);
                    if(reported>DateTimeOffset.UtcNow)resume=reported;
                }
                var retry=response.Headers.RetryAfter;
                var retryTime=retry?.Date ?? (retry?.Delta is TimeSpan delta?DateTimeOffset.UtcNow+delta:(DateTimeOffset?)null);
                if(retryTime>resume)resume=retryTime.Value;
                if(resume>blockedUntil)blockedUntil=resume;
            }
            return response;
        } finally {requestGate.Release();}
    }
}
