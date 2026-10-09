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
    private bool liveBusy,routeBusy;
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
        Get<ComboBox>("RouteMode").ItemsSource=new[]{"Stargates", "Gates + Ansiblex", "Capital jumps"};Get<ComboBox>("RouteMode").SelectedIndex=0;
        Get<ComboBox>("ShipPicker").ItemsSource=Navigation.Ships;Get<ComboBox>("ShipPicker").SelectedIndex=0;
        Get<ComboBox>("RouteMode").SelectionChanged+=(_,_)=>UpdateRange();Get<ComboBox>("ShipPicker").SelectionChanged+=(_,_)=>UpdateRange();Get<NumericUpDown>("Calibration").ValueChanged+=(_,_)=>UpdateRange();UpdateRange();
        Get<ComboBox>("ActivityLayer").ItemsSource=new[]{"Activity layer off","Ship kills · last hour","Pod kills · last hour","NPC kills · last hour","Ship jumps · last hour"};Get<ComboBox>("ActivityLayer").SelectedIndex=0;
        Get<ComboBox>("ActivityLayer").SelectionChanged+=(_,_)=>ApplyLive();
        foreach(var n in new[]{"WormholeLayer","StormLayer","KillLayer"})Get<CheckBox>(n).IsCheckedChanged+=(_,_)=>ApplyLive();
        Get<Button>("ImportBridges").Click+=async(_,_)=>await ImportBridgesAsync();
        Get<Button>("ClearBridges").Click+=(_,_)=>{try{PersistBridges([]);bridges=[];UpdateBridges();}catch(Exception e){SetStatus(e.Message);}};
        try{var path=Path.Combine(SettingsStore.Folder,"ansiblex.csv");if(File.Exists(path))bridges=Navigation.ParseBridges(File.ReadAllText(path),universe);}catch(Exception e){SetStatus("Ansiblex import unavailable: "+e.Message);}UpdateBridges();
        foreach(var name in new[]{"KillList","WormholeList","StormList"})
            WireSystemActivation(Get<ListBox>(name),item=>(item as LiveItem)?.System);
        Get<ListBox>("KillList").DoubleTapped+=(_,_)=>{if(Get<ListBox>("KillList").SelectedItem is LiveItem {Id:>0} i)Browse($"https://zkillboard.com/kill/{i.Id}/");};
        Get<ComboBox>("RegionPicker").SelectionChanged+=(_,_)=>ApplyLive();
        operationTimer.Tick+=(_,_)=>{_ = RefreshLive();};
        Opened+=(_,_)=>{operationTimer.Start();_ = RefreshLive();};
    }
    private void StopOperations(){operationTimer.Stop();liveApi.Dispose();}
    private void UpdateRange()
    {
        bool capital=Get<ComboBox>("RouteMode").SelectedIndex==2;
        Get<ComboBox>("ShipPicker").IsVisible=capital;Get<NumericUpDown>("Calibration").IsVisible=capital;Get<TextBlock>("JumpRangeLabel").IsVisible=capital;
        Get<CheckBox>("HighSecOnly").IsVisible=!capital;
        Get<TextBlock>("JumpRangeLabel").Text=$"Maximum {Navigation.Range(Math.Max(0,Get<ComboBox>("ShipPicker").SelectedIndex),(int)(Get<NumericUpDown>("Calibration").Value??5)):0.0} LY per jump";
    }
    private static string[] Names(string? value)=>(value??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
    private async void Plan()
    {
        if(routeBusy)return;routeBusy=true;Get<Button>("PlanRoute").IsEnabled=false;
        try
        {
            var from=Get<AutoCompleteBox>("FromBox").Text??"";var to=Get<AutoCompleteBox>("ToBox").Text??"";
            var waypoints=Names(Get<TextBox>("WaypointsBox").Text);var avoid=new HashSet<string>(Names(Get<TextBox>("AvoidBox").Text),StringComparer.OrdinalIgnoreCase);
            int mode=Get<ComboBox>("RouteMode").SelectedIndex;bool high=mode!=2 && Get<CheckBox>("HighSecOnly").IsChecked==true;
            double? range=mode==2?Navigation.Range(Get<ComboBox>("ShipPicker").SelectedIndex,(int)(Get<NumericUpDown>("Calibration").Value??5)):null;
            var network=mode==1?bridges:[];var highsecStart=Get<ComboBox>("ShipPicker").SelectedIndex is 2 or 4;
            var route=await Task.Run(()=>Navigation.Plan(universe,from,to,waypoints,avoid,high,network,range,highsecStart));
            if(closing.IsCancellationRequested)return;
            Map.Route=route.Systems;Map.RouteLegs=route.Legs;
            Get<ListBox>("RouteList").ItemsSource=route.Systems.Select((s,i)=>new LiveItem(s.Name,i==0?$"START  {s.Name}":$"{i:00}  {s.Name}\n{route.Legs[i-1].Kind}"+(mode==2?$" · {route.Legs[i-1].LightYears:0.00} LY":""))).ToArray();
            Get<TextBlock>("RouteSummary").Text=route.Systems.Count==0?"No route found with these restrictions.":$"{route.Legs.Count} hops · {route.Legs.Count(l=>l.Kind=="Ansiblex")} Ansiblex"+(mode==2?$"\n{route.Legs.Sum(l=>l.LightYears):0.00} LY total. Cynos, fuel, fatigue and access must be checked in game.":"\nBridge access, fuel and online status are not verified.");
            Map.InvalidateVisual();if(route.Systems.Count>0)SelectSystem(route.Systems[0].Name);
        }
        catch(Exception e){Map.Route=[];Map.RouteLegs=[];Map.InvalidateVisual();Get<ListBox>("RouteList").ItemsSource=null;Get<TextBlock>("RouteSummary").Text=e.Message;SetStatus("Route: "+e.Message);}
        finally{routeBusy=false;Get<Button>("PlanRoute").IsEnabled=true;}
    }
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
        Map.Activity=situation.Activity;Map.ActivityMode=Get<ComboBox>("ActivityLayer").SelectedIndex;
        Map.Wormholes=Get<CheckBox>("WormholeLayer").IsChecked==true?situation.Wormholes:[];
        Map.StormAreas=Get<CheckBox>("StormLayer").IsChecked==true?situation.StormAreas():new();
        Map.Kills=Get<CheckBox>("KillLayer").IsChecked==true?situation.Kills:[];
        var region=(Get<ComboBox>("RegionPicker").SelectedItem as Region)?.Name;
        Get<ListBox>("KillList").ItemsSource=situation.Kills.Where(k=>universe.ById.TryGetValue(k.SystemId,out var s) && s.Region==region).Select(k=>new LiveItem(universe.ById[k.SystemId].Name,$"{universe.ById[k.SystemId].Name} · {k.Time:HH:mm} UTC\n{ships.GetValueOrDefault(k.ShipType,$"Type {k.ShipType}")} · {k.Value:N0} ISK",k.Id)).ToArray();
        Get<ListBox>("WormholeList").ItemsSource=situation.Wormholes.Select(w=>new LiveItem(w.System,$"{w.System} ↔ {w.Hub}\n{w.InSignature} / {w.OutSignature} · {w.Size}\nExpires {w.Expires:MMM d HH:mm} UTC")).ToArray();
        Get<ListBox>("StormList").ItemsSource=situation.Storms.Select(s=>new LiveItem(s.System,$"{s.System} · {s.Type}\n{s.Name}")).ToArray();
        Get<TextBlock>("FeedStatus").Text=string.Join('\n',situation.Status.Values);Map.InvalidateVisual();UpdateOperationalDetails();
    }
    private void UpdateOperationalDetails()
    {
        if(selected==null || situation==null)return;var sys=universe.Systems[selected];
        Get<TextBlock>("SystemInfo").Text=$"{sys.Region}\nSecurity {sys.Security:0.00} · {sys.Jumps.Length} gates"+(sys.Station?"\nNPC station present":"")+(situation.Activity.TryGetValue(sys.Id,out var a)?$"\nLast hour: {a.Ships} ships · {a.Pods} pods\n{a.Npcs} NPCs · {a.Jumps} jumps":"");
    }
}
