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

public sealed record MonitorPrefs(string System="",string Coverage="Nearby",string Target="",int Jumps=3,int Minutes=5,bool Voice=false,bool Siren=false,bool Popup=false,string Message="Threat detected",double Volume=.7,string FriendlyNames="",bool TrustBadges=false);
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
        Closed+=(_,_)=>{monitorTimer.Stop();monitorWindow?.Close();};UpdateAlert();
    }
    public void ConfigureMonitor(MonitorPrefs prefs)
    {
        monitorPrefs=prefs with{Jumps=Math.Clamp(prefs.Jumps,1,30),Minutes=Math.Clamp(prefs.Minutes,1,30),Volume=Math.Clamp(prefs.Volume,0,1)};
        try{Directory.CreateDirectory(SettingsStore.Folder);File.WriteAllText(PrefPath+".tmp",JsonSerializer.Serialize(monitorPrefs));File.Move(PrefPath+".tmp",PrefPath,true);}catch(Exception e){SetStatus("Monitoring settings: "+e.Message);}
        localTracker.Reset();UpdateAlert();
    }
    public void StartMonitor(int id,double[] area,double[]? system)
    {
        latchedThreat=false;captureWindow=id;localArea=area;systemArea=system;screenSystem=null;localTracker.Reset();lastCapture=DateTimeOffset.MinValue;monitorRunning=true;UpdateAlert();
    }
    public void StopMonitor(){monitorRunning=false;latchedThreat=false;screenSystem=null;localTracker.Reset();UpdateAlert();}
    private async Task ScanLocal()
    {
        if(captureBusy||!monitorRunning||localArea==null)return;
        captureBusy=true;int id=captureWindow;var roi=localArea;
        try
        {
            var data=await NativeCapture.Call(new{action="capture",id,roi,preview=monitorWindow!=null});
            if(!monitorRunning||id!=captureWindow||!ReferenceEquals(roi,localArea))return;
            var friends=monitorPrefs.FriendlyNames.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows=data.GetProperty("rows").EnumerateArray().Select(r=>new LocalRow(r.GetProperty("name").GetString()!.Trim(),r.GetProperty("confidence").GetDouble(),r.GetProperty("standing").GetString()!))
                .Select(r=>r with{Standing=friends.Contains(r.Name)?"Friendly":monitorPrefs.TrustBadges?r.Standing:"Unknown"}).ToArray();
            localTracker.Observe(rows);latchedThreat |= localTracker.Threat;lastCapture=DateTimeOffset.UtcNow;
            monitorWindow?.ShowCapture(data,rows,localTracker.Healthy);
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
        catch(Exception e){localTracker.Reset();screenSystem=null;monitorWindow?.ShowError(e.Message);SetStatus("Local monitor: "+e.Message);}
        finally{captureBusy=false;UpdateAlert();}
    }
    private void UpdateAlert()
    {
        if(Get<Border>("TrafficLight")==null)return;
        bool fresh=monitorRunning&&DateTimeOffset.UtcNow-lastCapture<TimeSpan.FromSeconds(5);
        var location=systemArea!=null&&monitorRunning?screenSystem??"":monitorPrefs.System;
        var result=AlertEngine.Evaluate(universe,reports,new(location,monitorPrefs.Coverage,monitorPrefs.Target,monitorPrefs.Jumps,monitorPrefs.Minutes),DateTimeOffset.UtcNow,
            fresh&&localTracker.Healthy,monitorRunning&&latchedThreat,logsHealthy);
        Get<Border>("TrafficLight").Background=Brush.Parse(result.Level switch{"Red"=>"#A51E35","Yellow"=>"#D0A62C","Green"=>"#166B46",_=>"#364354"});
        Get<TextBlock>("AlertTitle").Text=result.Title;
        Get<TextBlock>("AlertTitle").Foreground=result.Level=="Yellow"?Brushes.Black:Brushes.White;
        Get<TextBlock>("AlertDetail").Text=result.Detail+(string.IsNullOrEmpty(location)?"":"\nMonitoring "+location+(systemArea!=null&&monitorRunning?" · screen":" · manual"));
        ToolTip.SetTip(Get<Border>("TrafficLight"),Get<TextBlock>("AlertDetail").Text);
        if(result.Level!=previousAlert&&result.Level is "Red" or "Yellow" && (result.Level=="Red" || DateTimeOffset.UtcNow-lastSound>TimeSpan.FromSeconds(15)))
        {
            lastSound=DateTimeOffset.UtcNow;
            if(monitorPrefs.Voice||monitorPrefs.Siren)_=SoundAlert(result.Title);
            if(monitorPrefs.Popup){var popup=new Window{Title=result.Title,Width=440,Height=190,Content=new TextBlock{Text=result.Title+"\n\n"+result.Detail,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(20)}};popup.Show(this);}
        }
        previousAlert=result.Level;
    }
    private async Task SoundAlert(string title)
    {
        try{await NativeCapture.Call(new{action="alert",voice=monitorPrefs.Voice,siren=monitorPrefs.Siren,message=title+". "+monitorPrefs.Message,volume=monitorPrefs.Volume});}
        catch(Exception e){SetStatus("Audio unavailable: "+e.Message);}
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
