using System.Text.Json;
namespace Smt.Core;
public sealed record KnownStructure(long Id,long SystemId,long TypeId,string Name,DateTimeOffset? LastSeen,bool Queryable);
public static class Facilities
{
    public const string SourceUrl="https://data.everef.net/structures/structures-latest.v2.json";
    public static bool ShowNpc(int mode)=>mode is 1 or 3;
    public static bool ShowUpwell(int mode)=>mode is 2 or 3;
    public static KnownStructure[] Parse(JsonElement root,Universe universe)
    {
        if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid structure dataset");
        var list=new List<KnownStructure>();
        foreach(var property in root.EnumerateObject())
        {
            var row=property.Value;if(row.ValueKind!=JsonValueKind.Object)continue;
            var id=row.Number("structure_id");if(id==0)long.TryParse(property.Name,out id);
            var system=row.Number("solar_system_id");var type=row.Number("type_id");
            if(id<=0||type<=0||!universe.ById.ContainsKey(system))continue;
            var seen=new[]{"last_structure_get","last_seen_public_structure","last_seen_market_structure"}.Select(k=>DateTimeOffset.TryParse(row.Str(k),out var at)?at:(DateTimeOffset?)null).Where(at=>at>new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero)).Max();
            list.Add(new(id,system,type,string.IsNullOrWhiteSpace(row.Str("name"))?$"Structure {id}":row.Str("name"),seen,row.TryGetProperty("is_gettable_structure",out var gettable)&&gettable.ValueKind==JsonValueKind.True));
        }
        return list.DistinctBy(s=>s.Id).OrderBy(s=>s.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string TypeName(long type)=>type switch{35832=>"Astrahus",35833=>"Fortizar",35834=>"Keepstar",35825=>"Raitaru",35826=>"Azbel",35827=>"Sotiyo",35835=>"Athanor",35836=>"Tatara",81826=>"Metenox",_=>$"Type {type}"};
}
