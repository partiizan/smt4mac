using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Smt.Core;
using System.Text.Json;
namespace Smt.Desktop;
public partial class MainWindow
{
    private readonly LiveApi facilityApi=new();
    private KnownStructure[] knownStructures=[];
    private bool facilityBusy;
    private DateTimeOffset nextFacilities;
    private string facilityHealth="Known structures · waiting for EVE Ref";
    private string FacilityCache=>Path.Combine(SettingsStore.Folder,"known-structures.json");
    private void InitializeFacilities()
    {
        try{using var saved=JsonDocument.Parse(File.ReadAllText(FacilityCache));knownStructures=Facilities.Parse(saved.RootElement,universe);facilityHealth="EVE Ref · cached data; refreshing";}catch(Exception e) when(e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException){ }
        var picker=Get<ComboBox>("FacilityFilter");picker.ItemsSource=new[]{"Facilities: Off","NPC stations","Known Upwell","NPC + known Upwell"};picker.SelectedIndex=Math.Clamp(settings.FacilityMode,0,3);
        picker.SelectionChanged+=(_,_)=>{settings=settings with{FacilityMode=Math.Max(0,picker.SelectedIndex)};Save();ApplyFacilities();};
        Get<Button>("FacilityDetails").Click+=(_,_)=>ShowFacilityDetails();
        Get<ComboBox>("RegionPicker").SelectionChanged+=(_,_)=>ApplyFacilities();
        Opened+=(_,_)=>_=RefreshFacilities();ApplyFacilities();
    }
    private async Task RefreshFacilities()
    {
        if(facilityBusy||closing.IsCancellationRequested||DateTimeOffset.UtcNow<nextFacilities)return;
        facilityBusy=true;nextFacilities=DateTimeOffset.UtcNow.AddMinutes(5);
        try{
            var data=await facilityApi.Json(Facilities.SourceUrl,closing.Token,3600);var parsed=Facilities.Parse(data,universe);
            if(closing.IsCancellationRequested)return;
            knownStructures=parsed;nextFacilities=DateTimeOffset.UtcNow.AddHours(1);facilityHealth=$"EVE Ref · checked {DateTimeOffset.UtcNow:MMM d HH:mm} UTC";
            try{Directory.CreateDirectory(SettingsStore.Folder);File.WriteAllText(FacilityCache+".tmp",data.GetRawText());File.Move(FacilityCache+".tmp",FacilityCache,true);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){facilityHealth+=" · disk cache unavailable";}
        }catch(Exception e) when(e is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException){facilityHealth="EVE Ref unavailable · retained structures may be stale";}
        finally{facilityBusy=false;if(!closing.IsCancellationRequested){ApplyFacilities();UpdateOperationalDetails();}}
    }
    private void ApplyFacilities()
    {
        Map.FacilityMode=settings.FacilityMode;Map.StructureCounts=knownStructures.GroupBy(s=>s.SystemId).ToDictionary(g=>g.Key,g=>g.Count());Map.InvalidateVisual();
        var region=(Get<ComboBox>("RegionPicker").SelectedItem as Region)?.Name;
        var npc=universe.Data.Systems.Count(s=>s.Region==region&&s.Station);var upwell=knownStructures.Count(s=>universe.ById[s.SystemId].Region==region);
        Get<TextBlock>("FacilityStatus").Text=$"□ NPC: {npc} systems (bundled map) · ◇ Upwell: {upwell} known structures · {facilityHealth}. Incomplete coverage; no marker does not mean no structures. Docking access unverified.";
        Get<TextBlock>("FacilityStatus").IsVisible=settings.FacilityMode!=0;
    }
    private string FacilitySummary(long systemId)
    {
        var found=knownStructures.Where(s=>s.SystemId==systemId).ToArray();return $"\nKnown Upwell: {found.Length}"+(found.Length>0?" · "+string.Join(", ",found.GroupBy(s=>Facilities.TypeName(s.TypeId)).Select(g=>$"{g.Count()} {g.Key}")):" (coverage incomplete)");
    }
    private void ShowFacilityDetails()
    {
        var region=(Get<ComboBox>("RegionPicker").SelectedItem as Region)?.Name;
        var panel=new StackPanel{Spacing=8,Margin=new Thickness(18)};
        panel.Children.Add(new TextBlock{Text=$"{region} · known facilities",FontSize=21,FontWeight=FontWeight.Bold});
        panel.Children.Add(new TextBlock{Text="NPC station presence comes from the bundled map. Upwell records come from EVE Ref and may be missing, stale or no longer dockable. Last seen is a source observation, not a live confirmation. Individual NPC station names/counts are not included in the bundled map.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text=facilityHealth,TextWrapping=TextWrapping.Wrap});
        var source=new Button{Content="Open EVE Ref source documentation ↗"};source.Click+=(_,_)=>Browse("https://docs.everef.net/datasets/structures");panel.Children.Add(source);
        var mode=settings.FacilityMode==0?3:settings.FacilityMode;
        var rows=new List<(string System,string Label)>();
        if(Facilities.ShowNpc(mode))rows.AddRange(universe.Data.Systems.Where(s=>s.Region==region&&s.Station).Select(s=>(s.Name,$"□ {s.Name} · NPC station present")));
        if(Facilities.ShowUpwell(mode))rows.AddRange(knownStructures.Where(s=>universe.ById[s.SystemId].Region==region).Select(s=>(universe.ById[s.SystemId].Name,$"◇ {universe.ById[s.SystemId].Name} · {Facilities.TypeName(s.TypeId)}\n{s.Name}\nLast seen: {(s.LastSeen is {} at?at.ToString("yyyy-MM-dd HH:mm 'UTC'"):"unknown")} · {(s.Queryable?"source could query":"source could not query; possibly stale")} · ID {s.Id}")));
        var dialog=new Window{Title="SMT4Mac · Known facilities",Width=760,Height=640};
        foreach(var row in rows.OrderBy(r=>r.System).ThenBy(r=>r.Label)){var button=new Button{Content=new TextBlock{Text=row.Label,TextWrapping=TextWrapping.Wrap},HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Left};button.Click+=(_,_)=>{SelectSystem(row.System);dialog.Close();};panel.Children.Add(button);}
        if(rows.Count==0)panel.Children.Add(new TextBlock{Text="No records in this region for the selected filter. Coverage is incomplete."});
        dialog.Content=new ScrollViewer{Content=panel};dialog.Show(this);
    }
}
