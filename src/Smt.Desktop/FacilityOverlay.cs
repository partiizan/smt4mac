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
        InitializeNamedFacilities();
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
        Map.FacilityMode=settings.FacilityMode;Map.StructureCounts=AllStructures().GroupBy(s=>s.SystemId).ToDictionary(g=>g.Key,g=>g.Count());Map.InvalidateVisual();
        var region=(Get<ComboBox>("RegionPicker").SelectedItem as Region)?.Name;
        var npc=universe.Data.Systems.Count(s=>s.Region==region&&s.Station);var upwell=AllStructures().Count(s=>universe.ById[s.SystemId].Region==region);
        Get<TextBlock>("FacilityStatus").Text=$"□ NPC: {npc} systems (bundled map) · ◇ Upwell: {upwell} known structures · {facilityHealth}. Incomplete coverage; no marker does not mean no structures. Docking access unverified.";
        Get<TextBlock>("FacilityStatus").IsVisible=settings.FacilityMode!=0;UpdateFacilityPane();
    }
    private string FacilitySummary(long systemId)
    {
        var found=AllStructures().Where(s=>s.SystemId==systemId).ToArray();return $"\nKnown Upwell: {found.Length}"+(found.Length>0?" · "+string.Join(", ",found.GroupBy(s=>Facilities.TypeName(s.TypeId)).Select(g=>$"{g.Count()} {g.Key}")):" (coverage incomplete)");
    }
}
