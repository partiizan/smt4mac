using System.Text.RegularExpressions;
namespace Smt.Core;

public sealed record AlertOptions(string System="", string Coverage="Nearby", string Target="", int Jumps=3, int Minutes=5);
public sealed record AlertResult(string Level,string Title,string Detail);
public static class AlertEngine
{
    public static bool IsThreat(IntelReport r) => !r.Clear && !r.Message.Contains('?') &&
        Regex.IsMatch(r.Message,@"\b(hostiles?|neutrals?|neuts?|reds?|tackl\w*|camp\w*|bubbles?|interceptors?|sabres?|lokis?|legions?|proteus|tengus?|gang|fleet|\d+\s*(?:in|on|local))\b",RegexOptions.IgnoreCase);
    public static AlertResult Evaluate(Universe u,IEnumerable<IntelReport> reports,AlertOptions o,DateTimeOffset now,bool localHealthy,bool localThreat,bool intelHealthy)
    {
        if(localThreat)return new("Red","RED ALERT","Neutral, hostile or unclassified pilot detected in visible Local. Verify in game; restart capture to reset this alert.");
        var current=u.Systems.GetValueOrDefault(o.System.Trim());
        var recent=reports.Where(r=>r.Time<=now && r.Time>=now.AddMinutes(-o.Minutes)).OrderBy(r=>r.Time);
        var latest=new Dictionary<string,IntelReport>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in recent)if(r.Clear||IsThreat(r))foreach(var s in r.Systems)latest[s]=r;
        var threats=latest.Where(x=>IsThreat(x.Value)).ToArray();
        string Detail(KeyValuePair<string,IntelReport> r)=>$"{r.Key} · {r.Value.Time:HH:mm:ss} UTC · {r.Value.Message}";
        if(current!=null && threats.FirstOrDefault(x=>x.Key==current.Name) is var here && here.Value!=null)return new("Red","RED ALERT",Detail(here));
        bool Match(string name)
        {
            if(o.Coverage=="System")return name.Equals(o.Target,StringComparison.OrdinalIgnoreCase);
            if(o.Coverage=="Constellation")return !string.IsNullOrEmpty(o.Target)&&u.Systems.GetValueOrDefault(name)?.Constellation==o.Target;
            if(current==null)return false;
            if(o.Coverage=="Follow constellation")return !string.IsNullOrEmpty(current.Constellation)&&u.Systems.GetValueOrDefault(name)?.Constellation==current.Constellation;
            var route=u.Route(current.Name,name);return route.Count>0&&route.Count-1<=o.Jumps;
        }
        var nearby=threats.Where(x=>Match(x.Key)).OrderByDescending(x=>x.Value.Time).ToArray();
        if(nearby.Length>0)return new("Yellow","HOSTILES NEARBY",Detail(nearby[0]));
        bool coverageReady=o.Coverage switch {"System"=>u.Systems.ContainsKey(o.Target),"Constellation"=>u.Data.Systems.Any(s=>s.Constellation==o.Target&&!string.IsNullOrEmpty(o.Target)),"Follow constellation"=>!string.IsNullOrEmpty(current?.Constellation),_=>current!=null};
        if(!localHealthy||!intelHealthy||current==null||!coverageReady)return new("Gray","MONITORING UNAVAILABLE","Check Local capture, current system, alert coverage and log access. Threat detections still take priority.");
        return new("Green","ALL CLEAR","No detected threats in visible Local or recent intel within your coverage. Not a guarantee of safety.");
    }
}

public sealed record LocalRow(string Name,double Confidence,string Standing);
public sealed class LocalTracker
{
    private HashSet<string> prior=[];
    public LocalRow[] Confirmed {get;private set;}=[];
    public bool Healthy {get;private set;}
    public void Reset(){prior.Clear();Confirmed=[];Healthy=false;}
    public void Observe(IEnumerable<LocalRow> rows)
    {
        var valid=rows.Where(r=>r.Name.Length>1).ToArray();
        Confirmed=valid.Where(r=>r.Confidence>=.75&&prior.Contains(r.Name.ToUpperInvariant())).ToArray();
        Healthy=valid.Length>0&&Confirmed.Length==valid.Length;
        prior=valid.Where(r=>r.Confidence>=.75).Select(r=>r.Name.ToUpperInvariant()).ToHashSet();
    }
    public bool Threat=>Confirmed.Any(r=>r.Standing!="Friendly");
}
