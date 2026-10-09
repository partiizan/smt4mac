using System.Net;
using System.Text.Json;
namespace Smt.Core;

// All callers share server cache and backoff state. Failed refreshes never masquerade as fresh data.
public sealed class LiveApi : IDisposable
{
    private readonly HttpClient http;
    private readonly Dictionary<string,(string Body,DateTimeOffset Until)> cache=[];
    private readonly Dictionary<string,DateTimeOffset> backoff=[];
    public LiveApi(HttpMessageHandler? handler=null)
    {
        http=handler==null?new():new(handler);http.Timeout=TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SMT-Mac-Beta/0.8 (+https://github.com/partiizan/smt4mac)");
    }
    public async Task<JsonElement> Json(string url,CancellationToken ct,int cacheSeconds=0)
    { using var doc=JsonDocument.Parse(await Text(url,ct,cacheSeconds)); return doc.RootElement.Clone(); }
    public async Task<string> Text(string url,CancellationToken ct,int cacheSeconds=0)
    {
        var host=new Uri(url).Host; var key=url;
        if(cache.TryGetValue(key,out var hit) && hit.Until>DateTimeOffset.UtcNow)return hit.Body;
        if(backoff.TryGetValue(host,out var until) && until>DateTimeOffset.UtcNow)throw new HttpRequestException($"{host}: retry after {until:HH:mm:ss} UTC.");
        using var req=new HttpRequestMessage(HttpMethod.Get,url);
        using var response=await http.SendAsync(req,ct);
        if(response.StatusCode==(HttpStatusCode)429 || response.StatusCode==(HttpStatusCode)420 || response.StatusCode==HttpStatusCode.ServiceUnavailable)
            backoff[host]=response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow+(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60));
        response.EnsureSuccessStatusCode();
        var body=await response.Content.ReadAsStringAsync(ct);
        var seconds=Math.Max(cacheSeconds,(response.Content.Headers.Expires-DateTimeOffset.UtcNow)?.TotalSeconds ?? response.Headers.CacheControl?.MaxAge?.TotalSeconds ?? 0);
        if(seconds>0)cache[key]=(body,DateTimeOffset.UtcNow.AddSeconds(seconds));
        return body;
    }
    public void Dispose()=>http.Dispose();
}
public static class JsonFields
{
    public static long Number(this JsonElement e,string key)=>e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.Number?v.GetInt64():0;
    public static string Str(this JsonElement e,string key)=>e.TryGetProperty(key,out var v)?v.ToString():"";
}
