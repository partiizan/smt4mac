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
            if(settings.FacilityMode==0||!universe.Systems.TryGetValue(name,out var sys))return name;
            var npc=npcStations.TryGetValue(sys.Id,out var stations)?$"{stations.Length} NPC stations":sys.Station?"NPC stations · names not loaded":"NPC stations · not checked";
            return $"{name}\n{npc}\n{AllStructures().Count(s=>s.SystemId==sys.Id)} known Upwell structures\nClick for named facilities · coverage incomplete";
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
    private void ShowNamedFacilityDetails()
    {
        var region=(Get<ComboBox>("RegionPicker").SelectedItem as Region)?.Name;
        var dialog=new Window{Title=$"SMT4Mac · {region} facilities",Width=800,Height=680};
        var root=new DockPanel{Margin=new Thickness(18)};
        var top=new StackPanel{Spacing=8};DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text=$"{region} · named facilities",FontSize=21,FontWeight=FontWeight.Bold});
        top.Children.Add(new TextBlock{Text="Public and session-only ESI results. Coverage is incomplete; a known structure does not guarantee docking access.",TextWrapping=TextWrapping.Wrap});
        var search=new TextBox{Watermark="Filter by system, facility name or type…"};top.Children.Add(search);
        var load=new Button{Content="Load NPC station names for this region"};top.Children.Add(load);
        var status=new TextBlock{Text=facilityHealth,TextWrapping=TextWrapping.Wrap};top.Children.Add(status);
        var rows=new StackPanel{Spacing=6};root.Children.Add(new ScrollViewer{Content=rows,Margin=new Thickness(0,10,0,0)});
        void Render()
        {
            rows.Children.Clear();var query=search.Text?.Trim()??"";
            var mode=settings.FacilityMode==0?3:settings.FacilityMode;
            foreach(var sys in universe.Data.Systems.Where(s=>s.Region==region).OrderBy(s=>s.Name)){
                var labels=new List<string>();
                if(Facilities.ShowNpc(mode)){
                    if(npcStations.TryGetValue(sys.Id,out var stations))labels.AddRange(stations.Select(s=>$"□ {s.Name} · NPC station · ID {s.Id} · ESI checked {s.Checked:MMM d HH:mm} UTC"));
                    else if(sys.Station)labels.Add("NPC station present · names not loaded");
                }
                if(Facilities.ShowUpwell(mode))labels.AddRange(AllStructures().Where(s=>s.SystemId==sys.Id).Select(UpwellText));
                foreach(var text in labels.Where(t=>(sys.Name+" "+t).Contains(query,StringComparison.OrdinalIgnoreCase))){
                    var button=new Button{Content=new TextBlock{Text=sys.Name+"\n"+text,TextWrapping=TextWrapping.Wrap},HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Left};
                    button.Click+=(_,_)=>{SelectSystem(sys.Name);dialog.Close();};rows.Children.Add(button);
                }
            }
            if(rows.Children.Count==0)rows.Children.Add(new TextBlock{Text="No matching records. Load NPC names or resolve known Upwell IDs through ESI."});
        }
        var cts=CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        search.TextChanged+=(_,_)=>Render();
        load.Click+=async(_,_)=>{
            load.IsEnabled=false;
            var systems=universe.Data.Systems.Where(s=>s.Region==region&&s.Station).ToArray();int done=0;
            foreach(var sys in systems){
                if(cts.IsCancellationRequested)break;
                status.Text=$"Loading NPC stations · {++done}/{systems.Length}";
                await LoadNpcStations(sys.Id,cts.Token);Render();
                if(npcErrors.ContainsKey(sys.Id)){status.Text=npcErrors[sys.Id];break;}
            }
            if(!cts.IsCancellationRequested){status.Text=$"NPC name lookup finished · {done}/{systems.Length} station-bearing systems checked. "+(npcErrors.Count>0?"Some records unavailable.":"");load.IsEnabled=true;}
        };
        dialog.Closed+=(_,_)=>cts.Cancel();Render();dialog.Content=root;dialog.Show(this);
    }
}
