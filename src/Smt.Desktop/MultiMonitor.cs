using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Smt.Core;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Smt.Desktop;

public sealed record ClientProfile(string Key,MonitorPrefs Prefs);
public sealed class ClientMonitor
{
    public string Key {get;}
    public MonitorPrefs Prefs {get;set;}
    public bool Running {get;private set;}
    public int WindowId {get;private set;}
    public double[]? Area {get;private set;}
    public double[]? SystemArea {get;private set;}
    public string? ScreenSystem {get;private set;}
    public DateTimeOffset LastCapture {get;private set;}
    public string Detail {get;private set;}="Stopped · choose an EVE window and Local area";
    public LocalTracker Tracker {get;}=new();
    public AlertNotifications Notifications {get;}=new();
    public MonitorWindow? SettingsWindow;
    public readonly StackPanel Card=new(){Spacing=6,Margin=new Thickness(0,12,0,0)};
    public readonly TextBlock Heading=new(){FontWeight=FontWeight.Bold,TextWrapping=TextWrapping.Wrap};
    public readonly TextBlock Local=new(){TextWrapping=TextWrapping.Wrap},Nearby=new(){TextWrapping=TextWrapping.Wrap},Health=new(){FontSize=10,TextWrapping=TextWrapping.Wrap};
    public readonly Border LocalBar=new(){Padding=new Thickness(9),CornerRadius=new CornerRadius(5)},AreaBar=new(){Padding=new Thickness(9),CornerRadius=new CornerRadius(5)};
    public readonly Image Preview=new(){MaxHeight=160,Stretch=Stretch.Uniform};
    private long generation;
    private Bitmap? bitmap;
    public ClientMonitor(string key,MonitorPrefs prefs){Key=key;Prefs=prefs;LocalBar.Child=Local;AreaBar.Child=Nearby;Card.Children.Add(Heading);Card.Children.Add(LocalBar);Card.Children.Add(AreaBar);Card.Children.Add(Preview);Card.Children.Add(Health);}
    public void Start(int id,double[] area,double[]? system){Stop();WindowId=id;Area=(double[])area.Clone();SystemArea=system==null?null:(double[])system.Clone();Running=true;Detail="Waiting for first scan";}
    public void Stop(){generation++;Running=false;LastCapture=default;ScreenSystem=null;Tracker.Reset();Notifications.ResetLocal();ClearPreview();Detail="Stopped";}
    public void ClearPreview(){Preview.Source=null;bitmap?.Dispose();bitmap=null;}
    public async Task Scan(Func<object,Task<JsonElement>> request,Universe universe)
    {
        if(!Running||Area==null)return;var current=generation;
        try{
            var data=await request(new{action="capture",id=WindowId,roi=Area,preview=true});
            if(current!=generation||!Running)return;
            if(SystemArea!=null){ScreenSystem=null;var location=await request(new{action="capture",id=WindowId,roi=SystemArea,preview=false});if(current!=generation||!Running)return;
                var names=location.GetProperty("rows").EnumerateArray().Where(r=>r.GetProperty("confidence").GetDouble()>=.85).SelectMany(r=>Regex.Matches(r.GetProperty("name").GetString()??"",@"[\p{L}\p{N}'-]+").Select(m=>m.Value)).Where(universe.Systems.ContainsKey).Select(n=>universe.Systems[n].Name).Distinct().ToArray();if(names.Length==1)ScreenSystem=names[0];}
            var rows=data.GetProperty("rows").EnumerateArray().Select(r=>new LocalRow(r.GetProperty("name").GetString()??"",r.GetProperty("confidence").GetDouble(),r.GetProperty("standing").GetString()??"Unknown")).ToArray();
            Tracker.Observe(rows);LastCapture=DateTimeOffset.UtcNow;Detail=$"{rows.Length} visible rows · checked {LastCapture:HH:mm:ss} UTC";
            if(data.TryGetProperty("png",out var png)&&!string.IsNullOrEmpty(png.GetString())){using var stream=new MemoryStream(Convert.FromBase64String(png.GetString()!));var next=new Bitmap(stream);ClearPreview();bitmap=next;Preview.Source=next;}
            SettingsWindow?.ShowCapture(data,rows,Tracker.Healthy);
        }catch(Exception e){if(current!=generation||!Running)return;LastCapture=default;Tracker.Reset();ScreenSystem=null;ClearPreview();Detail=e.Message;SettingsWindow?.ShowError(e.Message);}
    }
}
public partial class MainWindow
{
    private readonly List<ClientMonitor> clients=[];
    private bool scheduleBusy;
    private int scanCursor;
    private TimeSpan CaptureFreshness=>TimeSpan.FromSeconds(Math.Max(5,(clients.Count(c=>c.Running)+(monitorRunning?1:0))*4));
    private string ClientsPath=>Path.Combine(SettingsStore.Folder,"clients.json");
    private void InitializeClients()
    {
        try{foreach(var p in JsonSerializer.Deserialize<ClientProfile[]>(File.ReadAllText(ClientsPath))??[])AddClient(p,false);}catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){ }
        Get<Button>("AddMonitor").Click+=(_,_)=>{var client=AddClient(new(Guid.NewGuid().ToString(),monitorPrefs with{Label=$"Client {clients.Count+2}"}),true);OpenClient(client);};
    }
    private ClientMonitor AddClient(ClientProfile profile,bool save)
    {
        var client=new ClientMonitor(profile.Key,profile.Prefs);clients.Add(client);
        var buttons=new WrapPanel();var setup=new Button{Content="Configure…"};setup.Click+=(_,_)=>OpenClient(client);buttons.Children.Add(setup);
        var stop=new Button{Content="Stop"};stop.Click+=(_,_)=>{client.Stop();UpdateClients();};buttons.Children.Add(stop);
        var remove=new Button{Content="Remove"};remove.Click+=(_,_)=>{client.Stop();client.SettingsWindow?.Close();clients.Remove(client);Get<StackPanel>("ExtraMonitors").Children.Remove(client.Card);SaveClients();};buttons.Children.Add(remove);client.Card.Children.Add(buttons);
        Get<StackPanel>("ExtraMonitors").Children.Add(client.Card);if(save)SaveClients();UpdateClients();return client;
    }
    private void OpenClient(ClientMonitor client){if(client.SettingsWindow!=null){client.SettingsWindow.Activate();return;}client.SettingsWindow=new MonitorWindow(this,universe,client.Prefs,client);client.SettingsWindow.Closed+=(_,_)=>client.SettingsWindow=null;client.SettingsWindow.Show(this);}
    public void ConfigureClient(ClientMonitor client,MonitorPrefs prefs){client.Prefs=prefs with{System=prefs.System.Trim(),Minutes=Math.Clamp(prefs.Minutes,1,30)};SaveClients();UpdateClients();}
    private void SaveClients(){try{Directory.CreateDirectory(SettingsStore.Folder);File.WriteAllText(ClientsPath+".tmp",JsonSerializer.Serialize(clients.Select(c=>new ClientProfile(c.Key,c.Prefs)).ToArray()));File.Move(ClientsPath+".tmp",ClientsPath,true);}catch(Exception e){SetStatus("Client settings: "+e.Message);}}
    public bool WindowInUse(int id,ClientMonitor? except)=>clients.Any(c=>c!=except&&c.Running&&c.WindowId==id)||(except!=null&&monitorRunning&&captureWindow==id);
    private async Task ScanScheduled()
    {
        UpdateAlert();UpdateClients();if(scheduleBusy)return;scheduleBusy=true;
        try{var active=clients.Where(c=>c.Running).ToArray();var count=active.Length+(monitorRunning?1:0);if(count==0)return;var slot=scanCursor++%count;
            if(monitorRunning&&slot==0)await ScanLocal();else await active[slot-(monitorRunning?1:0)].Scan(captureRequest,universe);
        }finally{scheduleBusy=false;UpdateAlert();UpdateClients();}
    }
    private void UpdateClients()
    {
        var now=DateTimeOffset.UtcNow;
        foreach(var c in clients){var fresh=c.Running&&now-c.LastCapture<CaptureFreshness;if(!fresh){c.Tracker.Reset();c.ClearPreview();}
            var system=c.Running&&c.SystemArea!=null?c.ScreenSystem??"":c.Prefs.System;
            var panels=AlertEngine.Panels(universe,reports,system,c.Prefs.Minutes,now,fresh&&c.Tracker.Healthy,fresh&&c.Tracker.Threat,logsHealthy);
            c.Heading.Text=c.Prefs.Label+" · "+system;c.Local.Text="LOCAL · "+panels.Local.Title;c.LocalBar.Background=Brush.Parse(panels.Local.Level switch{"Red"=>"#A51E35","Green"=>"#166B46",_=>"#364354"});
            c.Nearby.Text="EXTENDED AREA · "+panels.Nearby!.Title+"\n"+panels.Nearby.Detail;c.AreaBar.Background=Brush.Parse(panels.Nearby.Level=="Yellow"?"#796019":"#364354");c.Health.Text=(fresh?"Live · ":c.Running?"Unavailable · ":"Stopped · ")+c.Detail;
            foreach(var level in c.Notifications.Observe(panels,now))_=NotifyAlert(level,level=="Red"?panels.Local.Detail:panels.Nearby.Detail,c.Prefs with{Volume=monitorPrefs.Volume,VoiceIdentifier=monitorPrefs.VoiceIdentifier});
        }
    }
}
