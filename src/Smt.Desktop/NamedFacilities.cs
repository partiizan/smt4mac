using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Smt.Core;
using System.Text.Json;
namespace Smt.Desktop;
public sealed record NpcStation(long Id,long SystemId,long TypeId,string Name,DateTimeOffset Checked);
public partial class MainWindow
{
    private readonly Dictionary<long,NpcStation[]> npcStations=[];
    private readonly HashSet<long> npcBusy=[];
    private readonly Dictionary<long,string> npcErrors=[];
    private string NpcCache=>Path.Combine(SettingsStore.Folder,"npc-stations.json");
    private void InitializeNamedFacilities()
    {
        try{var cached=JsonSerializer.Deserialize<Dictionary<long,NpcStation[]>>(File.ReadAllText(NpcCache));if(cached!=null)foreach(var pair in cached)npcStations[pair.Key]=pair.Value;}
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){}
        Get<Button>("StructureConnect").Click+=(_,_)=>ShowStructureConnector();
        Map.FacilityTooltip=name=>{
            if(!universe.Systems.TryGetValue(name,out var sys))return name;
            var npc=npcStations.TryGetValue(sys.Id,out var stations)?$"{stations.Length} NPC stations":sys.Station?"NPC stations · names not loaded":"NPC stations · not checked";
            var structures=AllStructures().Where(s=>s.SystemId==sys.Id).OrderBy(s=>s.Name,StringComparer.OrdinalIgnoreCase).ToArray();
            var details=structures.Length==0?"No known Upwell structures · coverage incomplete":string.Join("\n",structures.Select(s=>$"◇ {s.Name} · {Facilities.TypeName(s.TypeId)}"));
            return $"{name}\n{npc}\n{details}";
        };
    }
    private async Task LoadNpcStations(long systemId,CancellationToken ct)
    {
        if(npcBusy.Contains(systemId))return;
        if(npcStations.TryGetValue(systemId,out var existing)&&existing.Length>0&&existing.All(s=>s.Checked>DateTimeOffset.UtcNow.AddDays(-1)))return;
        npcBusy.Add(systemId);npcErrors.Remove(systemId);UpdateFacilityPane();
        try{
            var system=await facilityApi.Json($"https://esi.evetech.net/latest/universe/systems/{systemId}/",ct,86400);
            var result=new List<NpcStation>();
            if(system.TryGetProperty("stations",out var ids))foreach(var id in ids.EnumerateArray()){
                var d=await facilityApi.Json($"https://esi.evetech.net/latest/universe/stations/{id.GetInt64()}/",ct,86400);
                result.Add(new(id.GetInt64(),systemId,d.Number("type_id"),d.Str("name"),DateTimeOffset.UtcNow));
            }
            ct.ThrowIfCancellationRequested();npcStations[systemId]=result.ToArray();
            try{Directory.CreateDirectory(SettingsStore.Folder);File.WriteAllText(NpcCache+".tmp",JsonSerializer.Serialize(npcStations));File.Move(NpcCache+".tmp",NpcCache,true);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
        }catch(Exception e) when(e is HttpRequestException or JsonException or OperationCanceledException){npcErrors[systemId]="NPC names unavailable · retained records may be stale; select again to retry.";}
        finally{npcBusy.Remove(systemId);if(!closing.IsCancellationRequested){UpdateFacilityPane();Map.InvalidateVisual();}}
    }
    private string UpwellText(KnownStructure s)=>$"{s.Name} · {Facilities.TypeName(s.TypeId)}\n{(privateStructures.ContainsKey(s.Id)?"ESI verified this session":"EVE Ref observation")} · {(s.LastSeen is {} at?at.ToString("MMM d HH:mm 'UTC'"):"date unknown")} · ID {s.Id}";
    private string NamedFacilityText(long id)
    {
        var lines=new List<string>{"NPC STATIONS"};
        if(npcStations.TryGetValue(id,out var stations)){
            lines.AddRange(stations.Select(s=>$"□ {s.Name} · NPC station · ID {s.Id} · ESI checked {s.Checked:MMM d HH:mm} UTC"));
            if(stations.Length==0)lines.Add("No NPC stations returned by ESI.");
        }else lines.Add(npcBusy.Contains(id)?"Loading station names…":"Station names not loaded.");
        if(npcErrors.TryGetValue(id,out var error))lines.Add(error);
        lines.Add("UPWELL STRUCTURES");
        var upwell=AllStructures().Where(s=>s.SystemId==id).OrderBy(s=>s.Name).ToArray();
        lines.AddRange(upwell.Select(s=>"◇ "+UpwellText(s)));
        if(upwell.Length==0)lines.Add("No known records. This does not mean no structures exist.");
        return string.Join("\n",lines);
    }
    private void UpdateFacilityPane()
    {
        var pane=Get<ScrollViewer>("SelectedFacilities");
        pane.IsVisible=selected!=null;
        if(selected!=null&&universe.Systems.TryGetValue(selected,out var sys))Get<TextBlock>("SelectedFacilityText").Text=NamedFacilityText(sys.Id);
    }
}
