using System.Globalization;
namespace Smt.Core;

public sealed record Bridge(string From, string To);
public sealed record RouteLeg(StarSystem From, StarSystem To, string Kind, double LightYears = 0);
public sealed record NavigationResult(IReadOnlyList<StarSystem> Systems, IReadOnlyList<RouteLeg> Legs);
public static class Navigation
{
    public static readonly string[] Ships = ["Carrier / Dread / FAX", "Supercarrier / Titan", "Jump freighter", "Rorqual", "Black Ops", "Command carrier"];
    public static double Range(int ship, int calibration) => (ship switch { 0 => 3.5, 1 => 3, 2 => 5, 3 => 5, 4 => 4, 5 => 3.75, _ => throw new ArgumentOutOfRangeException(nameof(ship)) }) * (1 + .2 * Math.Clamp(calibration,0,5));
    public static double Distance(StarSystem a, StarSystem b) => a.ActualX is {} x && a.ActualY is {} y && a.ActualZ is {} z && b.ActualX is {} bx && b.ActualY is {} by && b.ActualZ is {} bz ? Math.Sqrt(Math.Pow(x-bx,2)+Math.Pow(y-by,2)+Math.Pow(z-bz,2)) : double.PositiveInfinity;
    public static bool JumpDestination(StarSystem s) => s.Security < .45 && JumpSpace(s);
    public static bool JumpSpace(StarSystem s) => s.Region is not "Pochven" and not "A821-A" and not "J7HZ-F" and not "UUA-F4" && s.Id < 31000000;
    public static Bridge[] ParseBridges(string text, Universe u)
    {
        var result=new List<Bridge>(); var row=0;
        foreach(var raw in text.Split('\n'))
        {
            row++;var line=raw.Trim();if(line.Length==0 || line.StartsWith('#')) continue;
            var parts=line.Split(',',StringSplitOptions.TrimEntries);
            if(parts.Length!=2 || !u.Systems.TryGetValue(parts[0],out var a) || !u.Systems.TryGetValue(parts[1],out var b) || a==b)
                throw new FormatException($"Line {row}: use two different system names separated by a comma.");
            if(a.Security>=.45 || b.Security>=.45) throw new FormatException($"Line {row}: Ansiblex endpoints must be low/null security.");
            result.Add(new(a.Name,b.Name));
        }
        return result.Distinct().ToArray();
    }
    public static NavigationResult Plan(Universe u, string from, string to, IEnumerable<string> waypoints, ISet<string> avoid, bool high, IReadOnlyList<Bridge> bridges, double? capitalRange=null, bool allowHighsecStart=false)
    {
        var stops=new[]{from}.Concat(waypoints).Append(to).Select(s=>s.Trim()).ToArray();
        if(stops.Any(s=>!u.Systems.ContainsKey(s))) throw new ArgumentException("A start, destination, or waypoint is not a known system.");
        if(avoid.Any(s=>!u.Systems.ContainsKey(s))) throw new ArgumentException("An avoided system name is not recognized.");
        if(stops.Any(avoid.Contains)) throw new ArgumentException("A required stop is also in the avoidance list.");
        if(capitalRange is {} range && (!double.IsFinite(range) || range<=0 || range>10)) throw new ArgumentException("Jump range must be greater than zero and at most 10 LY.");
        if(capitalRange!=null && (!JumpSpace(u.Systems[stops[0]]) || stops.Skip(allowHighsecStart?1:0).Any(s=>!JumpDestination(u.Systems[s])))) throw new ArgumentException("Capital stops must be accessible low/null-sec systems outside Pochven.");
        var systems=new List<StarSystem>(); var legs=new List<RouteLeg>();
        for(int i=1;i<stops.Length;i++)
        {
            var start=u.Systems[stops[i-1]];var end=u.Systems[stops[i]];
            if(high && (start.Security<.45 || end.Security<.45)) return new([],[]);
            var previous=new Dictionary<string,RouteLeg?>(StringComparer.OrdinalIgnoreCase) {[start.Name]=null};
            var queue=new Queue<StarSystem>();queue.Enqueue(start);
            while(queue.TryDequeue(out var cur))
            {
                if(cur==end) break;
                IEnumerable<RouteLeg> next;
                if(capitalRange is {} ly)
                    next=u.Data.Systems.Where(s=>JumpDestination(s) && s!=cur).Select(s=>new RouteLeg(cur,s,"Cyno",Distance(cur,s))).Where(l=>l.LightYears<=ly+1e-9);
                else
                    next=cur.Jumps.Where(u.Systems.ContainsKey).Select(n=>new RouteLeg(cur,u.Systems[n],"Gate"))
                      .Concat(bridges.Where(b=>b.From==cur.Name || b.To==cur.Name).Select(b=>new RouteLeg(cur,u.Systems[b.From==cur.Name?b.To:b.From],"Ansiblex")));
                foreach(var l in next)
                {
                    if(previous.ContainsKey(l.To.Name) || avoid.Contains(l.To.Name) || high && l.To.Security<.45) continue;
                    previous[l.To.Name]=l;queue.Enqueue(l.To);
                }
            }
            if(!previous.ContainsKey(end.Name)) return new([],[]);
            var segment=new List<RouteLeg>();for(var name=end.Name;previous[name] is {} leg;name=leg.From.Name) segment.Add(leg);
            segment.Reverse();if(systems.Count==0) systems.Add(start);foreach(var l in segment) {legs.Add(l);systems.Add(l.To);}
        }
        return new(systems,legs);
    }
}
