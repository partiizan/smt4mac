using System.Text.RegularExpressions;
namespace Smt.Core;

public sealed record AlertOptions(string System="", string Coverage="Nearby", string Target="", int Jumps=3, int Minutes=5);
public sealed record AlertResult(string Level,string Title,string Detail);
public sealed record AlertPanels(AlertResult Local,AlertResult? Nearby,string Coverage,AlertResult Overall);
public static class AlertEngine
{
    public static AlertPanels Panels(Universe u,IEnumerable<IntelReport> reports,string system,int minutes,DateTimeOffset now,bool localHealthy,bool localThreat,bool intelHealthy)
    {
        var feed=reports.ToArray();
        var options=new AlertOptions(system,"Automatic",Jumps:3,Minutes:minutes);
        var overall=Evaluate(u,feed,options,now,localHealthy,localThreat,intelHealthy);
        var nearby=Evaluate(u,feed.Select(r=>r with{Systems=r.Systems.Where(s=>!s.Equals(system.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray()}),options,now,true,false,intelHealthy);
        var local=overall.Level=="Red"?overall:localHealthy?new AlertResult("Green","LOCAL CLEAR","No hostile badge detected in the visible Local roster."):new AlertResult("Gray","MONITORING UNAVAILABLE","Local capture is stopped, stale or uncertain.");
        var current=u.Systems.GetValueOrDefault(system.Trim());
        var coverage=current==null?"Enter a valid current system in settings.":$"{current.Name} · {current.Constellation} constellation + 3 stargate jumps";
        if(!intelHealthy)coverage+=" · Selected intel logs unavailable";
        return new(local,nearby.Level=="Yellow"?nearby:null,coverage,overall);
    }
    public static bool IsThreat(IntelReport r) => !r.Clear && !r.Message.Contains('?') &&
        Regex.IsMatch(r.Message,@"\b(hostiles?|neutrals?|neuts?|reds?|tackl\w*|camp\w*|bubbles?|interceptors?|sabres?|lokis?|legions?|proteus|tengus?|gang|fleet|\d+\s*(?:in|on|local))\b",RegexOptions.IgnoreCase);
    public static AlertResult Evaluate(Universe u,IEnumerable<IntelReport> reports,AlertOptions o,DateTimeOffset now,bool localHealthy,bool localThreat,bool intelHealthy)
    {
        if(localThreat)return new("Red","RED ALERT","Hostile standing badge detected in visible Local. Verify in game; restart capture to reset this alert.");
        var current=u.Systems.GetValueOrDefault(o.System.Trim());
        var recent=reports.Where(r=>r.Time<=now && r.Time>=now.AddMinutes(-o.Minutes)).OrderBy(r=>r.Time);
        var latest=new Dictionary<string,IntelReport>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in recent)if((r.Clear&&!r.Message.Contains('?'))||IsThreat(r))foreach(var s in r.Systems)latest[s]=r;
        var threats=latest.Where(x=>IsThreat(x.Value)).ToArray();
        string Detail(KeyValuePair<string,IntelReport> r)=>$"{r.Key} · {r.Value.Time:HH:mm:ss} UTC · {r.Value.Message}";
        if(current!=null && threats.FirstOrDefault(x=>x.Key==current.Name) is var here && here.Value!=null)return new("Red","RED ALERT",Detail(here));
        var inRange=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(current!=null)
        {
            inRange.Add(current.Name);var frontier=new[]{current.Name};
            for(var jump=0;jump<o.Jumps && frontier.Length>0;jump++)frontier=frontier.SelectMany(n=>u.Systems[n].Jumps).Where(n=>u.Systems.ContainsKey(n)&&inRange.Add(n)).ToArray();
        }
        bool Match(string name)
        {
            if(o.Coverage=="System")return name.Equals(o.Target,StringComparison.OrdinalIgnoreCase);
            if(o.Coverage=="Constellation")return !string.IsNullOrEmpty(o.Target)&&u.Systems.GetValueOrDefault(name)?.Constellation==o.Target;
            if(current==null)return false;
            if(o.Coverage=="Follow constellation")return !string.IsNullOrEmpty(current.Constellation)&&u.Systems.GetValueOrDefault(name)?.Constellation==current.Constellation;
            if(o.Coverage=="Automatic" && !string.IsNullOrEmpty(current.Constellation) && u.Systems.GetValueOrDefault(name)?.Constellation==current.Constellation)return true;
            return inRange.Contains(name);
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
    private HashSet<string> priorHostiles=[];
    public LocalRow[] Confirmed {get;private set;}=[];
    public LocalRow[] ThreatRows {get;private set;}=[];
    public bool Healthy {get;private set;}
    public void Reset(){prior.Clear();priorHostiles.Clear();Confirmed=[];ThreatRows=[];Healthy=false;}
    private static string Key(LocalRow row)=>Regex.Replace(row.Name.Trim(),@"\s+"," ").ToUpperInvariant();
    public void Observe(IEnumerable<LocalRow> rows)
    {
        var valid=rows.Where(r=>r.Name.Length>1).ToArray();
        // A visible hostile badge must belong to the same recognized pilot on consecutive scans.
        // Unknown geometry is a capture warning, never evidence of a hostile.
        ThreatRows=valid.Where(r=>r.Confidence>=.75 && r.Standing=="Hostile" && priorHostiles.Contains(Key(r))).ToArray();
        Confirmed=valid.Where(r=>r.Confidence>=.75 && prior.Contains(Key(r))).ToArray();
        Healthy=valid.Length>0 && Confirmed.Length==valid.Length && valid.All(r=>r.Standing is "Friendly" or "Ignored" || (r.Standing=="Hostile" && ThreatRows.Any(t=>Key(t)==Key(r))));
        prior=valid.Where(r=>r.Confidence>=.75).Select(Key).ToHashSet();
        priorHostiles=valid.Where(r=>r.Confidence>=.75 && r.Standing=="Hostile").Select(Key).ToHashSet();
    }
    public bool Threat=>ThreatRows.Length>0;
}

public static class CaptureArea
{
    public static double[] Normalize(double x1,double y1,double x2,double y2,double width,double height)
    {
        x1=Math.Clamp(x1,0,width);x2=Math.Clamp(x2,0,width);y1=Math.Clamp(y1,0,height);y2=Math.Clamp(y2,0,height);
        return [Math.Min(x1,x2)/width,Math.Min(y1,y2)/height,Math.Abs(x1-x2)/width,Math.Abs(y1-y2)/height];
    }
}
