using System.Text.Json;

namespace Smt.Core;

public sealed record StarSystem(long Id, string Name, string Region, double Security, double X, double Y, bool Station, string[] Jumps, double? ActualX = null, double? ActualY = null, double? ActualZ = null);
public sealed record MapNode(string Name, double X, double Y, bool Outside);
public sealed record Region(string Name, string Faction, MapNode[] Nodes)
{
    public override string ToString() => Name;
}
public sealed record UniverseData(string Source, string Commit, StarSystem[] Systems, Region[] Regions);

public sealed class Universe
{
    public UniverseData Data { get; }
    public Dictionary<string, StarSystem> Systems { get; }
    public Dictionary<long, StarSystem> ById { get; }
    public Universe(string filename)
    {
        Data = JsonSerializer.Deserialize<UniverseData>(File.ReadAllText(filename), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The universe snapshot could not be read.");
        Systems = Data.Systems.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        ById = Data.Systems.ToDictionary(s => s.Id);
    }
    public StarSystem[] Search(string query) => string.IsNullOrWhiteSpace(query) ? [] : Data.Systems
        .Where(s => s.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(s => !s.Name.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase)).ThenBy(s => s.Name).Take(12).ToArray();

    // Minimum stargate jumps only. No wormholes, Ansiblex, or jump-drive connections.
    public IReadOnlyList<StarSystem> Route(string from, string to, bool highSecOnly = false)
    {
        if (!Systems.TryGetValue(from.Trim(), out var start) || !Systems.TryGetValue(to.Trim(), out var end)) return [];
        if (highSecOnly && (start.Security < .45 || end.Security < .45)) return [];
        var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [start.Name] = null };
        var queue = new Queue<StarSystem>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            if (current.Name == end.Name)
            {
                var result = new List<StarSystem>();
                for (string? name = current.Name; name != null; name = previous[name]) result.Add(Systems[name]);
                result.Reverse(); return result;
            }
            foreach (var name in current.Jumps)
            {
                if (previous.ContainsKey(name) || !Systems.TryGetValue(name, out var next) || (highSecOnly && next.Security < .45)) continue;
                previous[name] = current.Name; queue.Enqueue(next);
            }
        }
        return [];
    }
}
