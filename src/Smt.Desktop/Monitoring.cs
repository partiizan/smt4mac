using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Smt.Core;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Smt.Desktop;

public sealed record ConditionAlert(bool Voice=false,bool Sound=false,bool Popup=false,string Message="");
public sealed record MonitorPrefs(string System="",string Coverage="Nearby",string Target="",int Jumps=3,int Minutes=5,bool Voice=false,bool Siren=false,bool Popup=false,string Message="Threat detected",double Volume=.7,ConditionAlert? Yellow=null,ConditionAlert? Red=null)
{
    public ConditionAlert YellowAlert=>Yellow??new(Voice,Siren,Popup,"Caution, hostiles in the nearby area");
    public ConditionAlert RedAlert=>Red??new(Voice,Siren,Popup,"Alert, hostiles entering local");
}
public sealed record CaptureWindow(int Id,string Title){public override string ToString()=>Title;}
public partial class MainWindow
{
    private MonitorPrefs monitorPrefs=new();
    private readonly LocalTracker localTracker=new();
    private DateTimeOffset lastCapture=DateTimeOffset.MinValue,lastSound=DateTimeOffset.MinValue;
    private readonly DispatcherTimer monitorTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private bool captureBusy,monitorRunning,logsHealthy,latchedThreat;
    private int captureWindow;
    private double[]? localArea,systemArea;
    private string? screenSystem;
    private string previousAlert="Gray";
    private string triggerDetail="";
    private Bitmap? localPreview;
    private string captureDetail="Local capture has not been started.";
    private MonitorWindow? monitorWindow;
    private string PrefPath=>Path.Combine(SettingsStore.Folder,"monitoring.json");
    private void InitializeMonitoring()
    {
        try{monitorPrefs=JsonSerializer.Deserialize<MonitorPrefs>(File.ReadAllText(PrefPath))??new();}catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){}
        Get<Button>("MonitorSetup").Click+=(_,_)=>
        {
            if(monitorWindow!=null){monitorWindow.Activate();return;}
            monitorWindow=new MonitorWindow(this,universe,monitorPrefs);
            monitorWindow.Closed+=(_,_)=>monitorWindow=null;monitorWindow.Show(this);
        };
        Get<TextBox>("IntelSearch").TextChanged+=(_,_)=>RefreshIntel();
        monitorTimer.Tick+=async(_,_)=>{UpdateAlert();await ScanLocal();};monitorTimer.Start();
        Closed+=(_,_)=>{monitorTimer.Stop();monitorWindow?.Close();ClearLocalPreview();};UpdateAlert();
    }
    public void ConfigureMonitor(MonitorPrefs prefs)
    {
        monitorPrefs=prefs with{System=prefs.System.Trim(),Coverage="Automatic",Target=universe.Systems.GetValueOrDefault(prefs.System.Trim())?.Constellation??"",Jumps=3,Minutes=Math.Clamp(prefs.Minutes,1,30),Volume=Math.Clamp(prefs.Volume,0,1)};
        try{Directory.CreateDirectory(SettingsStore.Folder);File.WriteAllText(PrefPath+".tmp",JsonSerializer.Serialize(monitorPrefs));File.Move(PrefPath+".tmp",PrefPath,true);}catch(Exception e){SetStatus("Monitoring settings: "+e.Message);}
        localTracker.Reset();UpdateAlert();
    }
    public void StartMonitor(int id,double[] area,double[]? system)
    {
        ClearLocalPreview();triggerDetail="";captureDetail="Waiting for first Local capture…";latchedThreat=false;captureWindow=id;localArea=area;systemArea=system;screenSystem=null;localTracker.Reset();lastCapture=DateTimeOffset.MinValue;monitorRunning=true;UpdateAlert();
    }
    public void StopMonitor(){ClearLocalPreview();triggerDetail="";captureDetail="Local capture is stopped.";monitorRunning=false;latchedThreat=false;screenSystem=null;localTracker.Reset();UpdateAlert();}
    private async Task ScanLocal()
    {
        if(captureBusy||!monitorRunning||localArea==null)return;
        captureBusy=true;int id=captureWindow;var roi=localArea;
        try
        {
            var data=await NativeCapture.Call(new{action="capture",id,roi,preview=true});
            if(!monitorRunning||id!=captureWindow||!ReferenceEquals(roi,localArea))return;
            ApplyLocalCapture(data);
            if(systemArea!=null)
            {
                screenSystem=null;
                var location=await NativeCapture.Call(new{action="capture",id,roi=systemArea,preview=false});
                var candidates=location.GetProperty("rows").EnumerateArray().Where(r=>r.GetProperty("confidence").GetDouble()>=.85)
                    .SelectMany(r=>Regex.Matches(r.GetProperty("name").GetString()??"",@"[\p{L}\p{N}'-]+").Select(m=>m.Value))
                    .Where(n=>universe.Systems.ContainsKey(n)).Select(n=>universe.Systems[n].Name).Distinct().ToArray();
                if(candidates.Length==1)screenSystem=candidates[0];
            }
        }
        catch(Exception e){ClearLocalPreview();lastCapture=DateTimeOffset.MinValue;captureDetail=e.Message;localTracker.Reset();screenSystem=null;monitorWindow?.ShowError(e.Message);SetStatus("Local monitor: "+e.Message);}
        finally{captureBusy=false;UpdateAlert();}
    }
    private void ClearLocalPreview()
    {
        Get<Image>("LocalPreview").Source=null;localPreview?.Dispose();localPreview=null;
        Get<TextBlock>("PreviewPlaceholder").IsVisible=true;
    }
    public void ApplyLocalCapture(JsonElement data)
    {
        var rows=data.GetProperty("rows").EnumerateArray().Select(r=>new LocalRow(r.GetProperty("name").GetString()!.Trim(),r.GetProperty("confidence").GetDouble(),r.GetProperty("standing").GetString()!)).ToArray();
        captureDetail=rows.Length==0?"No roster names recognized. Check the crop and EVE text size.":$"{rows.Length} visible rows · {rows.Count(r=>r.Standing=="Friendly")} friendly · {rows.Count(r=>r.Standing=="Ignored")} unbadged · {rows.Count(r=>r.Standing=="Unknown")} uncertain";
        localTracker.Observe(rows);
        if(localTracker.Threat){latchedThreat=true;triggerDetail="Confirmed hostile badge: "+string.Join(", ",localTracker.ThreatRows.Select(r=>r.Name));}
        lastCapture=DateTimeOffset.UtcNow;
        if(data.TryGetProperty("png",out var png) && !string.IsNullOrEmpty(png.GetString()))
        {
            using var stream=new MemoryStream(Convert.FromBase64String(png.GetString()!));
            var next=new Bitmap(stream);var old=localPreview;localPreview=next;Get<Image>("LocalPreview").Source=next;old?.Dispose();
            Get<TextBlock>("PreviewPlaceholder").IsVisible=false;
        }
        monitorWindow?.ShowCapture(data,rows,localTracker.Healthy);UpdateAlert();
    }
    private void UpdateAlert()
    {
        if(Get<Border>("TrafficLight")==null)return;
        var now=DateTimeOffset.UtcNow;
        bool fresh=monitorRunning&&now-lastCapture<TimeSpan.FromSeconds(5);
        if(!fresh && localPreview!=null)ClearLocalPreview();
        var location=systemArea!=null&&monitorRunning?screenSystem??"":monitorPrefs.System;
        var panels=AlertEngine.Panels(universe,reports,location,monitorPrefs.Minutes,now,fresh&&localTracker.Healthy,monitorRunning&&latchedThreat,logsHealthy);
        var local=panels.Local;
        Get<Border>("TrafficLight").Background=Brush.Parse(local.Level switch{"Red"=>"#A51E35","Green"=>"#166B46",_=>"#364354"});
        Get<TextBlock>("AlertTitle").Text=local.Title;
        Get<TextBlock>("AlertDetail").Text=local.Level=="Gray"?captureDetail+(monitorRunning&&!fresh?" · waiting for fresh scan":""):local.Detail;
        Get<Border>("NearbyTag").IsVisible=panels.Nearby!=null;
        Get<TextBlock>("NearbyDetail").Text=panels.Nearby?.Detail??"";
        Get<TextBlock>("MonitorLocation").Text=panels.Coverage;
        Get<TextBlock>("CaptureStatus").Text=(fresh?$"Live · {lastCapture:HH:mm:ss} UTC · ":monitorRunning?"Waiting · ":"Stopped · ")+captureDetail;
        Get<TextBlock>("TriggerDetail").IsVisible=monitorRunning&&latchedThreat;
        Get<TextBlock>("TriggerDetail").Text=triggerDetail;
        var result=panels.Overall;
        if(result.Level!=previousAlert&&result.Level is "Red" or "Yellow" && (result.Level=="Red" || now-lastSound>TimeSpan.FromSeconds(15)))
        {lastSound=now;_=NotifyAlert(result.Level,result.Detail);}
        previousAlert=result.Level;
    }
    public async Task TestAlert(string level,MonitorPrefs prefs)=>await NotifyAlert(level,"Alert test",prefs);
    private async Task NotifyAlert(string level,string detail,MonitorPrefs? prefs=null)
    {
        prefs??=monitorPrefs;
        var alert=level=="Red"?prefs.RedAlert:prefs.YellowAlert;
        var title=level=="Red"?"RED ALERT":"HOSTILES NEARBY";
        if(alert.Popup){var popup=new Window{Title=title,Width=440,Height=230,Content=new TextBlock{Text=title+"\n\n"+alert.Message+"\n\n"+detail,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(20)}};popup.Show(this);}
        if(alert.Voice||alert.Sound)
        {
            try{await NativeCapture.Call(new{action="alert",voice=alert.Voice,siren=alert.Sound,message=alert.Message,volume=prefs.Volume});}
            catch(Exception e){SetStatus("Audio unavailable: "+e.Message);}
        }
    }
    private Control IntelView(IntelReport r,string query)
    {
        TextBlock Highlight(string text,int size,IBrush color)
        {
            var block=new TextBlock{FontSize=size,Foreground=color,TextWrapping=TextWrapping.Wrap};
            int start=0,index;
            if(query.Length>0)while((index=text.IndexOf(query,start,StringComparison.OrdinalIgnoreCase))>=0)
            {
                block.Inlines!.Add(new Run(text[start..index]));block.Inlines.Add(new Run(text.Substring(index,query.Length)){Foreground=Brush.Parse("#F2CE65"),FontWeight=FontWeight.Bold});start=index+query.Length;
            }
            block.Inlines!.Add(new Run(text[start..]));return block;
        }
        var stack=new StackPanel{Spacing=4,Margin=new Thickness(0,5)};
        stack.Children.Add(Highlight(string.Join(" · ",r.Systems)+(r.Clear?" / CLEAR REPORTED":""),12,Brushes.White));
        stack.Children.Add(Highlight(r.Message,12,Brush.Parse("#C5D6E8")));
        stack.Children.Add(Highlight($"{r.Time:MMM d HH:mm:ss} UTC · {r.Speaker} · {r.Source}",10,Brush.Parse("#92A3B8")));
        return stack;
    }
}
