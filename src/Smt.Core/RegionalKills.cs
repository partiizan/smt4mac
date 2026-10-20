using System.Text.Json;
namespace Smt.Core;
// Regional backfill is cached independently from the live R2Z2 sequence.
public sealed class RegionalKills(LiveApi api,Universe universe)
{
    private readonly Dictionary<string,long> regions=[];
    private readonly Dictionary<string,List<Kill>> history=[];
    private readonly Dictionary<string,DateTimeOffset> next=[];
    private readonly Dictionary<string,string> status=[];
    public string Status(string region)=>status.GetValueOrDefault(region,"Regional kills · waiting for first check");
    public IReadOnlyList<Kill> For(string region)=>history.GetValueOrDefault(region)??[];
    public async Task Refresh(string region,CancellationToken ct)
    {
        if(next.GetValueOrDefault(region)>DateTimeOffset.UtcNow)return;
        next[region]=DateTimeOffset.UtcNow.AddMinutes(2);
        try{
            if(!regions.TryGetValue(region,out var id)){
                var system=universe.Data.Systems.First(s=>s.Region==region);
                var sys=await api.Json($"https://esi.evetech.net/latest/universe/systems/{system.Id}/",ct,86400);
                var constellation=await api.Json($"https://esi.evetech.net/latest/universe/constellations/{sys.Number("constellation_id")}/",ct,86400);
                id=constellation.Number("region_id");if(id<=0)throw new InvalidDataException("Region lookup failed");regions[region]=id;
            }
            var data=await api.Json($"https://zkillboard.com/api/regionID/{id}/pastSeconds/86400/",ct,3600);
            if(data.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("zKillboard did not return a kill list");
            var found=new List<Kill>();
            foreach(var entry in data.EnumerateArray()){
                var kill=Situational.ParseKill(entry);
                // Older servers may return only a killmail hash; use public ESI, never SSO.
                if(kill==null&&entry.TryGetProperty("zkb",out var zkb)&&zkb.Str("hash") is var hash&&System.Text.RegularExpressions.Regex.IsMatch(hash,"^[a-fA-F0-9]{40}$")&&entry.Number("killmail_id")>0){
                    var esi=await api.Json($"https://esi.evetech.net/latest/killmails/{entry.Number("killmail_id")}/{hash}/",ct,86400);
                    kill=Situational.ParseKill(JsonSerializer.SerializeToElement(new{killmail_id=entry.Number("killmail_id"),esi,zkb}));
                }
                if(kill!=null&&universe.ById.GetValueOrDefault(kill.SystemId)?.Region==region)found.Add(kill);
            }
            history[region]=found.DistinctBy(k=>k.Id).OrderByDescending(k=>k.Time).Take(200).ToList();
            next[region]=DateTimeOffset.UtcNow.AddHours(1);status[region]=$"zKillboard · history checked {DateTimeOffset.UtcNow:HH:mm} UTC · up to 200 kills / 24h; cached 1h, reports delayed";
        }catch(OperationCanceledException){status[region]="zKillboard · timed out / cancelled; retained history may be stale";}
        catch(Exception e) when(e is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException){status[region]="zKillboard · unavailable; retained history may be stale ("+e.GetType().Name+")";}
    }
    public static Kill[] Merge(IEnumerable<Kill> history,IEnumerable<Kill> live,Universe universe,string region,DateTimeOffset now)=>history.Concat(live).Where(k=>k.Time>=now.AddHours(-24)&&k.Time<=now&&universe.ById.GetValueOrDefault(k.SystemId)?.Region==region).DistinctBy(k=>k.Id).OrderByDescending(k=>k.Time).Take(200).ToArray();
}
