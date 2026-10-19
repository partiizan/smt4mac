using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Smt.Core;
namespace Smt.Desktop;
public sealed record LiveItem(string System,string Label,long Id=0){public override string ToString()=>Label;}
public partial class MainWindow
{
    private readonly LiveApi liveApi=new();
    private Situational situation=null!;
    private readonly DispatcherTimer operationTimer=new(){Interval=TimeSpan.FromSeconds(6)};
    private readonly Dictionary<long,string> ships=[];
    private Bridge[] bridges=[];
    private bool liveBusy;
    private static void Browse(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    private void InitializeOperations()
    {
        try
        {
            var shipPath=Path.Combine(AppContext.BaseDirectory,"data","ship-types.json");
            if(!File.Exists(shipPath))shipPath=Path.Combine(AppContext.BaseDirectory,"..","Resources","data","ship-types.json");
            foreach(var entry in JsonSerializer.Deserialize<Dictionary<long,string>>(File.ReadAllText(shipPath))??[])ships[entry.Key]=entry.Value;
        }
        catch(Exception e) when(e is IOException or JsonException){SetStatus("Ship names unavailable; using type IDs.");}
        situation=new(liveApi,universe);
        Get<ComboBox>("WormholeHub").ItemsSource=new[]{"All hubs","Thera","Turnur"};
        Get<ComboBox>("WormholeHub").SelectedIndex=0;
        Get<ComboBox>("WormholeHub").SelectionChanged+=(_,_)=>RefreshWormholeRows();
        Get<TextBox>("WormholeSearch").TextChanged+=(_,_)=>RefreshWormholeRows();
        Get<Button>("EveScoutLink").Click+=(_,_)=>Browse("https://www.eve-scout.com/");
        Get<Button>("ImportBridges").Click+=async(_,_)=>await ImportBridgesAsync();
        Get<Button>("ClearBridges").Click+=(_,_)=>{try{PersistBridges([]);bridges=[];UpdateBridges();}catch(Exception e){SetStatus(e.Message);}};
        try{var path=Path.Combine(SettingsStore.Folder,"ansiblex.csv");if(File.Exists(path))bridges=Navigation.ParseBridges(File.ReadAllText(path),universe);}catch(Exception e){SetStatus("Ansiblex import unavailable: "+e.Message);}UpdateBridges();
        foreach(var name in new[]{"KillList","StormList"})
            WireSystemActivation(Get<ListBox>(name),item=>(item as LiveItem)?.System);
        Get<ListBox>("KillList").DoubleTapped+=(_,_)=>{if(Get<ListBox>("KillList").SelectedItem is LiveItem {Id:>0} i)Browse($"https://zkillboard.com/kill/{i.Id}/");};
        Get<ComboBox>("RegionPicker").SelectionChanged+=(_,_)=>ApplyLive();
        operationTimer.Tick+=(_,_)=>{RefreshWormholeRows();_ = RefreshLive();};
        Opened+=(_,_)=>{operationTimer.Start();_ = RefreshLive();};
    }
    private void StopOperations(){operationTimer.Stop();liveApi.Dispose();}
    private void PersistBridges(Bridge[] value)
    {
        Directory.CreateDirectory(SettingsStore.Folder);var path=Path.Combine(SettingsStore.Folder,"ansiblex.csv");File.WriteAllText(path+".tmp",string.Join('\n',value.Select(b=>b.From+","+b.To)));File.Move(path+".tmp",path,true);
    }
    private async Task ImportBridgesAsync()
    {
        try
        {
            var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Ansiblex CSV: FromSystem,ToSystem (one bidirectional link per line)",AllowMultiple=false});
            var path=files.FirstOrDefault()?.TryGetLocalPath();if(path==null)return;
            var parsed=Navigation.ParseBridges(await File.ReadAllTextAsync(path),universe);PersistBridges(parsed);bridges=parsed;UpdateBridges();
        }
        catch(Exception e){Get<TextBlock>("BridgeStatus").Text="Import rejected: "+e.Message;}
    }
    private void UpdateBridges(){Get<TextBlock>("BridgeStatus").Text=$"{bridges.Length} imported bidirectional links · access unverified";Map.Bridges=bridges;Map.InvalidateVisual();}
    private async Task RefreshLive()
    {
        if(liveBusy || closing.IsCancellationRequested)return;liveBusy=true;
        try{await situation.Refresh(closing.Token);if(!closing.IsCancellationRequested)ApplyLive();}
        catch(Exception e){if(!closing.IsCancellationRequested)Get<TextBlock>("FeedStatus").Text="Feed refresh: "+e.Message;}
        finally{liveBusy=false;}
    }
    private void ApplyLive()
    {
        if(situation==null)return;
        Map.Wormholes=situation.Wormholes;
        Map.StormAreas=situation.StormAreas();
        Map.Kills=situation.Kills;
        var region=(Get<ComboBox>("RegionPicker").SelectedItem as Region)?.Name;
        Get<ListBox>("KillList").ItemsSource=situation.Kills.Where(k=>universe.ById.TryGetValue(k.SystemId,out var s) && s.Region==region).Select(k=>new LiveItem(universe.ById[k.SystemId].Name,$"{universe.ById[k.SystemId].Name} · {k.Time:HH:mm} UTC\n{ships.GetValueOrDefault(k.ShipType,$"Type {k.ShipType}")} · {k.Value:N0} ISK",k.Id)).ToArray();
        Get<ListBox>("StormList").ItemsSource=situation.Storms.Select(s=>new LiveItem(s.System,$"{s.System} · {s.Type}\n{s.Name}")).ToArray();
        RefreshWormholeRows();
        Get<TextBlock>("FeedStatus").Text=string.Join('\n',situation.Status.Values);Map.InvalidateVisual();UpdateOperationalDetails();
    }
    private void UpdateOperationalDetails()
    {
        if(selected==null || situation==null)return;var sys=universe.Systems[selected];
        Get<TextBlock>("SystemInfo").Text=$"{sys.Region}\nSecurity {sys.Security:0.00} · {sys.Jumps.Length} gates"+(sys.Station?"\nNPC station present":"");
    }
}
