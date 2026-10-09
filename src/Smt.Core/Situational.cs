using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Smt.Core;
public sealed record Activity(long SystemId,int Ships,int Pods,int Npcs,int Jumps);
public sealed record Wormhole(string Hub,string System,string InSignature,string OutSignature,string Size,DateTimeOffset Expires);
public sealed record Storm(string System,string Name,string Type);
public sealed record Kill(long Id,long SystemId,long ShipType,DateTimeOffset Time,double Value);
public sealed class Situational(LiveApi api,Universe universe)
{
    public Dictionary<long,Activity> Activity {get;private set;}=[];
    public List<Wormhole> Wormholes {get;private set;}=[];
    public List<Storm> Storms {get;private set;}=[];
    public List<Kill> Kills {get;}=[];
    public Dictionary<string,string> Status {get;}=[];
    private readonly Dictionary<string,DateTimeOffset> next=[];
    private long sequence;
    public async Task Refresh(CancellationToken ct)
    {
        await Attempt("Activity",TimeSpan.FromMinutes(5),async()=>
        {
            var kills=await api.Json("https://esi.evetech.net/latest/universe/system_kills/",ct,cacheSeconds:300);
            var jumps=await api.Json("https://esi.evetech.net/latest/universe/system_jumps/",ct,cacheSeconds:300);
            Activity=ParseActivity(kills,jumps);
        });
        await Attempt("Wormholes",TimeSpan.FromMinutes(2),async()=>
        {
            var result=new List<Wormhole>();
            foreach(var hub in new[]{"Thera","Turnur"})result.AddRange(ParseWormholes(await api.Json("https://api.eve-scout.com/v2/public/signatures?system_name="+hub,ct,cacheSeconds:120),hub,DateTimeOffset.UtcNow));
            Wormholes=result;
        });
        await Attempt("Storms",TimeSpan.FromMinutes(15),async()=>
        {
            var html=await api.Text("https://evescoutrescue.com/home/stormtrack.php",ct,cacheSeconds:900);
            var storms=ParseStorms(html,universe);
            if(storms.Count==0)throw new InvalidDataException("No recognizable storm rows; source may have changed.");
            Storms=storms;
        });
        await Attempt("Kill feed",TimeSpan.FromSeconds(6),async()=>
        {
            if(sequence==0){sequence=(await api.Json("https://r2z2.zkillboard.com/ephemeral/sequence.json",ct)).Number("sequence");if(sequence<=0)throw new InvalidDataException("Invalid kill-feed sequence.");}
            for(int i=0;i<15;i++)
            {
                JsonElement data;
                try{data=await api.Json($"https://r2z2.zkillboard.com/ephemeral/{sequence}.json",ct);}
                catch(HttpRequestException e) when(e.StatusCode==HttpStatusCode.NotFound){break;}
                catch(HttpRequestException e) when(e.StatusCode==HttpStatusCode.Gone){sequence=0;break;}
                var kill=ParseKill(data);sequence++;
                if(kill!=null && !Kills.Any(k=>k.Id==kill.Id))Kills.Insert(0,kill);
            }
            Kills.RemoveAll(k=>k.Time<DateTimeOffset.UtcNow.AddHours(-1));if(Kills.Count>300)Kills.RemoveRange(300,Kills.Count-300);
        });
        Wormholes.RemoveAll(w=>w.Expires<=DateTimeOffset.UtcNow);
    }
    private async Task Attempt(string name,TimeSpan interval,Func<Task> action)
    {
        if(next.TryGetValue(name,out var at) && at>DateTimeOffset.UtcNow)return;
        next[name]=DateTimeOffset.UtcNow+interval;
        try{await action();Status[name]=$"{name} · checked {DateTimeOffset.UtcNow:HH:mm:ss} UTC";}
        catch(OperationCanceledException){Status[name]=$"{name} · timed out / cancelled; retained data may be stale";}
        catch(Exception e) when(e is HttpRequestException or JsonException or InvalidDataException or FormatException or InvalidOperationException or RegexMatchTimeoutException)
        {Status[name]=$"{name} · unavailable; retained data may be stale ({e.GetType().Name})";next[name]=DateTimeOffset.UtcNow.AddSeconds(60);}
    }
    public static Dictionary<long,Activity> ParseActivity(JsonElement kills,JsonElement jumps)
    {
        var result=new Dictionary<long,Activity>();
        foreach(var k in kills.EnumerateArray()){var id=k.Number("system_id");result[id]=new(id,(int)k.Number("ship_kills"),(int)k.Number("pod_kills"),(int)k.Number("npc_kills"),0);}
        foreach(var j in jumps.EnumerateArray()){var id=j.Number("system_id");result[id]=(result.GetValueOrDefault(id)??new(id,0,0,0,0)) with{Jumps=(int)j.Number("ship_jumps")};}
        return result;
    }
    public static List<Wormhole> ParseWormholes(JsonElement root,string hub,DateTimeOffset now)
    {
        var list=new List<Wormhole>();
        foreach(var w in root.EnumerateArray())
        {
            if(w.Str("signature_type")!="wormhole" || !DateTimeOffset.TryParse(w.Str("expires_at"),out var expiry) || expiry<=now || w.Str("in_system_name")=="" || w.TryGetProperty("completed",out var done) && done.ValueKind==JsonValueKind.False)continue;
            list.Add(new(hub,w.Str("in_system_name"),w.Str("in_signature"),w.Str("out_signature"),w.Str("max_ship_size"),expiry));
        }
        return list;
    }
    public static List<Storm> ParseStorms(string html,Universe u)
    {
        var result=new List<Storm>();
        foreach(Match row in Regex.Matches(html,"<tr\\b[^>]*>(.*?)</tr>",RegexOptions.Singleline|RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2)))
        {
            var cells=Regex.Matches(row.Groups[1].Value,"<td\\b[^>]*>(.*?)</td>",RegexOptions.Singleline|RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2)).Select(m=>WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value,"<[^>]*>","",RegexOptions.None,TimeSpan.FromSeconds(1))).Trim()).ToArray();
            if(cells.Length>=4 && u.Systems.TryGetValue(cells[1],out var s))result.Add(new(s.Name,cells[2],cells[3]));
        }
        return result;
    }
    public static Kill? ParseKill(JsonElement root)
    {
        if(!root.TryGetProperty("esi",out var esi) || esi.ValueKind!=JsonValueKind.Object || !esi.TryGetProperty("victim",out var victim) || !DateTimeOffset.TryParse(esi.Str("killmail_time"),out var time))return null;
        var value=root.TryGetProperty("zkb",out var zkb) && zkb.TryGetProperty("totalValue",out var v)?v.GetDouble():0;
        return new(root.Number("killmail_id"),esi.Number("solar_system_id"),victim.Number("ship_type_id"),time,value);
    }
    public Dictionary<string,int> StormAreas()
    {
        var areas=new Dictionary<string,int>();
        foreach(var storm in Storms)
        {
            var visited=new HashSet<string>{storm.System};var q=new Queue<(string,int)>();q.Enqueue((storm.System,0));
            while(q.TryDequeue(out var item))
            {
                var strength=item.Item2<=1?2:1;areas[item.Item1]=Math.Max(areas.GetValueOrDefault(item.Item1),strength);
                if(item.Item2==3 || !universe.Systems.TryGetValue(item.Item1,out var s))continue;
                foreach(var n in s.Jumps)if(visited.Add(n))q.Enqueue((n,item.Item2+1));
            }
        }
        return areas;
    }
}
