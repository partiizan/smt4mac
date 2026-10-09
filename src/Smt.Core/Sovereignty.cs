using System.Net;
using System.Text.Json;

namespace Smt.Core;

public sealed record SystemSovereignty(long SystemId, string Kind, double? Adm, int? Military, int? Industrial, int? Strategic, bool Capital);
public sealed record SovereigntySnapshot(DateTimeOffset CheckedAt, string? ETag, SystemSovereignty[] Systems);

/// <summary>Public ESI sovereignty, compatibility date 2026-05-19. No tokens or character data.</summary>
public sealed class SovereigntyClient : IDisposable
{
    public const string Endpoint = "https://esi.evetech.net/sovereignty/systems";
    private readonly HttpClient http;
    private readonly string cacheFile;
    private readonly Func<DateTimeOffset> clock;
    private string? etag;
    public IReadOnlyDictionary<long, SystemSovereignty> Systems { get; private set; } = new Dictionary<long, SystemSovereignty>();
    public DateTimeOffset? CheckedAt { get; private set; }
    public DateTimeOffset NextCheck { get; private set; }
    public bool FromDisk { get; private set; }
    public string? Error { get; private set; }
    public bool IsStale => Error != null || FromDisk || CheckedAt == null || clock()-CheckedAt > TimeSpan.FromMinutes(10);
    public SovereigntyClient(string cacheFile, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    {
        this.cacheFile=cacheFile; this.clock=clock ?? (()=>DateTimeOffset.UtcNow);
        http=handler==null?new HttpClient():new HttpClient(handler);
        http.Timeout=TimeSpan.FromSeconds(25); http.MaxResponseContentBufferSize=8*1024*1024;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SMT-Mac-Beta/0.8.0");
        http.DefaultRequestHeaders.Add("X-Compatibility-Date","2026-05-19");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        try
        {
            var saved=JsonSerializer.Deserialize<SovereigntySnapshot>(File.ReadAllText(cacheFile));
            if(saved!=null && saved.CheckedAt<=this.clock().AddMinutes(1))
            {
                Systems=saved.Systems.ToDictionary(s=>s.SystemId); CheckedAt=saved.CheckedAt; etag=saved.ETag; FromDisk=true;
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
    }
    public static Dictionary<long,SystemSovereignty> Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);
        if(!doc.RootElement.TryGetProperty("solar_systems",out var rows) || rows.ValueKind!=JsonValueKind.Array)
            throw new JsonException("Unexpected sovereignty response; existing values kept.");
        var result=new Dictionary<long,SystemSovereignty>();
        foreach(var row in rows.EnumerateArray())
        {
            long id=row.GetProperty("solar_system_id").GetInt64();
            string kind="Unclaimed"; double? adm=null; int? military=null,industrial=null,strategic=null; bool capital=false;
            if(row.TryGetProperty("claim",out var claim))
            {
                if(claim.TryGetProperty("alliance",out var alliance))
                {
                    kind="Alliance";
                    capital=alliance.TryGetProperty("is_capital_system",out var cap) && cap.ValueKind==JsonValueKind.True;
                    if(alliance.TryGetProperty("development",out var development))
                    {
                        if(development.TryGetProperty("activity_defense_multiplier",out var a) && a.TryGetDouble(out var n) && double.IsFinite(n) && n>=1 && n<=6) adm=n;
                        military=Index(development,"military_level"); industrial=Index(development,"industrial_level"); strategic=Index(development,"strategic_level");
                    }
                }
                else if(claim.TryGetProperty("faction",out _)) kind="Faction";
                else kind="Unknown";
            }
            if(!result.TryAdd(id,new(id,kind,adm,military,industrial,strategic,capital))) throw new JsonException("Duplicate system in sovereignty response.");
        }
        if(result.Count==0) throw new JsonException("Empty sovereignty response; existing values kept.");
        return result;
    }
    private static int? Index(JsonElement d,string key) => d.TryGetProperty(key,out var v) && v.TryGetInt32(out int n) && n>=0 && n<=5 ? n : null;
    public async Task RefreshAsync(CancellationToken token=default)
    {
        if(clock()<NextCheck) return;
        NextCheck=clock().AddMinutes(5);
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,Endpoint);
            if(etag!=null) request.Headers.TryAddWithoutValidation("If-None-Match",etag);
            using var response=await http.SendAsync(request,token);
            if(response.Headers.RetryAfter is {} retry)
            {
                var until=retry.Date ?? clock()+(retry.Delta ?? TimeSpan.Zero);
                if(until>NextCheck) NextCheck=until;
            }
            if(response.StatusCode==HttpStatusCode.NotModified)
            {
                if(CheckedAt==null) throw new HttpRequestException("ESI returned no data.");
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var parsed=Parse(await response.Content.ReadAsStringAsync(token));
                Systems=parsed; etag=response.Headers.ETag?.ToString();
            }
            var ttl=response.Headers.CacheControl?.MaxAge;
            if(ttl>TimeSpan.FromMinutes(5)) NextCheck=clock()+ttl.Value;
            CheckedAt=clock(); FromDisk=false; Error=null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                File.WriteAllText(cacheFile+".tmp",JsonSerializer.Serialize(new SovereigntySnapshot(CheckedAt.Value,etag,Systems.Values.ToArray())));
                File.Move(cacheFile+".tmp",cacheFile,true);
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException) { /* Live values remain usable if cache is unwritable. */ }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { Error=e is TaskCanceledException?"ESI request timed out":e.Message; }
    }
    public void Dispose()=>http.Dispose();
}
