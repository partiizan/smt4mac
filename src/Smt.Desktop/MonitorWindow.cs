using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Smt.Core;
using System.Text.Json;
namespace Smt.Desktop;

public sealed class MonitorWindow:Window
{
    private readonly MainWindow owner;
    private readonly ComboBox windows=new(){HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};
    private readonly Image preview=new(){Stretch=Stretch.Fill,Width=800,Height=450};
    private readonly Image live=new(){Stretch=Stretch.Uniform,Height=120};
    private readonly Canvas overlay=new(){Width=800,Height=450,Background=Brushes.Transparent};
    private readonly Border selection=new(){BorderBrush=Brushes.Cyan,BorderThickness=new Thickness(2),IsHitTestVisible=false};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock roster=new(){TextWrapping=TextWrapping.Wrap};
    private readonly ComboBox areaMode=new(){ItemsSource=new[]{"Local member list","System name (optional)"},SelectedIndex=0};
    private readonly AutoCompleteBox system=new(){Watermark="Current system (manual fallback)",MinimumPrefixLength=1,FilterMode=AutoCompleteFilterMode.StartsWithOrdinal};
    private readonly ComboBox coverage=new(){ItemsSource=new[]{"Nearby","System","Constellation","Follow constellation"},SelectedIndex=0};
    private readonly AutoCompleteBox target=new(){Watermark="Yellow target system / constellation",MinimumPrefixLength=1,FilterMode=AutoCompleteFilterMode.ContainsOrdinal};
    private readonly NumericUpDown jumps=new(){Minimum=1,Maximum=30,Value=3,FormatString="'Nearby jumps: '0"},minutes=new(){Minimum=1,Maximum=30,Value=5,FormatString="'Intel expiry minutes: '0"};
    private readonly CheckBox voice=new(){Content="Voice"},siren=new(){Content="Sound"},popup=new(){Content="Popup"};
    private readonly TextBox message=new(){Watermark="Spoken alert message"};
    private readonly Slider volume=new(){Minimum=0,Maximum=1,Value=.7};
    private Point? drag;
    private double[]? localArea,systemArea;
    private int? previewId;
    public MonitorWindow(MainWindow owner,Universe universe,MonitorPrefs prefs)
    {
        this.owner=owner;Title="SMT4Mac · Local monitoring & alerts";Width=880;Height=880;MinWidth=850;Background=Brush.Parse("#0E1723");
        system.ItemsSource=universe.Systems.Keys.Order().ToArray();system.Text=prefs.System;
        coverage.SelectedItem=prefs.Coverage;target.Text=prefs.Target;jumps.Value=prefs.Jumps;minutes.Value=prefs.Minutes;voice.IsChecked=prefs.Voice;siren.IsChecked=prefs.Siren;popup.IsChecked=prefs.Popup;message.Text=prefs.Message;volume.Value=prefs.Volume;
        void Targets(){target.ItemsSource=(coverage.SelectedItem as string) is "Constellation" or "Follow constellation"?universe.Data.Systems.Select(s=>s.Constellation).Where(s=>s.Length>0).Distinct().Order().ToArray():universe.Systems.Keys.Order().ToArray();}
        Targets();coverage.SelectionChanged+=(_,_)=>Targets();
        var stack=new StackPanel{Spacing=10,Margin=new Thickness(20)};
        Content=new ScrollViewer{Content=stack,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        stack.Children.Add(new TextBlock{Text="LOCAL SCREEN MONITOR · BETA",FontSize=20,FontWeight=FontWeight.Bold});
        stack.Children.Add(new TextBlock{Text="Select an EVE window, load its preview, then drag around only the Local member names and standing icons. Exclude chat, headers and counts. Optionally select a separate area containing only your system name. Screenshots stay on this Mac; none are saved or uploaded.",TextWrapping=TextWrapping.Wrap});
        stack.Children.Add(windows);
        var buttons=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8};stack.Children.Add(buttons);
        Button Add(string text,Action action){var b=new Button{Content=text};b.Click+=(_,_)=>action();buttons.Children.Add(b);return b;}
        Add("Find EVE windows",async()=>await Find());Add("Load preview",async()=>await Preview());Add("Start monitoring",()=>Start());Add("Stop capture",()=>{owner.StopMonitor();status.Text="Capture stopped; intel ingestion continues.";});
        stack.Children.Add(areaMode);
        var grid=new Grid{Width=800,Height=450};grid.Children.Add(preview);grid.Children.Add(overlay);overlay.Children.Add(selection);stack.Children.Add(grid);
        overlay.PointerPressed+=(_,e)=>{if(previewId==null)return;drag=e.GetPosition(overlay);e.Pointer.Capture(overlay);};
        overlay.PointerMoved+=(_,e)=>{if(drag is {} a){var p=e.GetPosition(overlay);Draw(a,p);}};
        overlay.PointerReleased+=(_,e)=>
        {
            if(drag is not {} a)return;var p=e.GetPosition(overlay);drag=null;e.Pointer.Capture(null);
            double x=Math.Clamp(Math.Min(a.X,p.X),0,800),y=Math.Clamp(Math.Min(a.Y,p.Y),0,450),w=Math.Min(800-x,Math.Abs(p.X-a.X)),h=Math.Min(450-y,Math.Abs(p.Y-a.Y));
            if(w<10||h<10){status.Text="Area too small; drag a larger rectangle.";return;}
            double[] roi=[x/800,y/450,w/800,h/450];
            if(areaMode.SelectedIndex==0)localArea=roi;else systemArea=roi;
            status.Text=(areaMode.SelectedIndex==0?"Local":"System")+" area selected. Press Start monitoring to apply.";
        };
        stack.Children.Add(status);stack.Children.Add(live);stack.Children.Add(roster);
        stack.Children.Add(new TextBlock{Text="Nullsec rules: green, blue and purple icons are friendly. Red, grey, orange, yellow and neutral/no-icon rows trigger Red after two matching scans. Verify recognition against your EVE settings. Red Local alerts stay latched until you verify in game and restart capture. A pilot no longer visible is not confirmed to have left. Keep the entire Local roster visible where possible.",TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#E8BE76")});
        stack.Children.Add(system);
        stack.Children.Add(new TextBlock{Text="YELLOW COVERAGE",FontWeight=FontWeight.Bold});stack.Children.Add(coverage);stack.Children.Add(target);stack.Children.Add(jumps);stack.Children.Add(minutes);
        var alerts=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=15};alerts.Children.Add(voice);alerts.Children.Add(siren);alerts.Children.Add(popup);stack.Children.Add(alerts);stack.Children.Add(message);stack.Children.Add(new TextBlock{Text="Voice volume"});stack.Children.Add(volume);
        var save=new Button{Content="Save alert settings"};save.Click+=(_,_)=>{owner.ConfigureMonitor(Prefs());status.Text="Alert settings saved. No ESI login is used.";};stack.Children.Add(save);
        var clear=new Button{Content="Clear system capture area (use manual system)"};clear.Click+=(_,_)=>{systemArea=null;status.Text="System area cleared; press Start monitoring to apply.";};stack.Children.Add(clear);
        var test=new Button{Content="Test audio"};test.Click+=async(_,_)=>{try{await NativeCapture.Call(new{action="alert",voice=voice.IsChecked==true,siren=siren.IsChecked==true,message=message.Text??"Test alert",volume=volume.Value});}catch(Exception e){ShowError(e.Message);}};stack.Children.Add(test);
    }
    private MonitorPrefs Prefs()=>new(system.Text?.Trim()??"",coverage.SelectedItem as string??"Nearby",target.Text?.Trim()??"",(int)(jumps.Value??3),(int)(minutes.Value??5),voice.IsChecked==true,siren.IsChecked==true,popup.IsChecked==true,message.Text??"Threat detected",volume.Value);
    private void Draw(Point a,Point b){Canvas.SetLeft(selection,Math.Clamp(Math.Min(a.X,b.X),0,800));Canvas.SetTop(selection,Math.Clamp(Math.Min(a.Y,b.Y),0,450));selection.Width=Math.Min(800,Math.Abs(a.X-b.X));selection.Height=Math.Min(450,Math.Abs(a.Y-b.Y));}
    private async Task Find(){try{var d=await NativeCapture.Call(new{action="list"});windows.ItemsSource=d.GetProperty("windows").EnumerateArray().Select(w=>new CaptureWindow(w.GetProperty("id").GetInt32(),w.GetProperty("title").GetString()!)).ToArray();windows.SelectedIndex=0;status.Text="Choose the character's window, then load its preview.";}catch(Exception e){ShowError(e.Message);}}
    private async Task Preview()
    {
        if(windows.SelectedItem is not CaptureWindow w){ShowError("Choose an EVE window first.");return;}
        try{var d=await NativeCapture.Call(new{action="capture",id=w.Id,preview=true,scan=false});SetImage(preview,d);previewId=w.Id;localArea=null;systemArea=null;selection.Width=0;selection.Height=0;status.Text="Drag around Local names plus standing icons.";}catch(Exception e){ShowError(e.Message);}
    }
    private void Start(){if(windows.SelectedItem is not CaptureWindow w||w.Id!=previewId||localArea==null){ShowError("Load this window's preview and select a Local area first.");return;}owner.ConfigureMonitor(Prefs());owner.StartMonitor(w.Id,localArea,systemArea);status.Text="Monitoring selected window. Leave Local visible and reselect the area if its layout changes.";}
    private static void SetImage(Image image,JsonElement d){if(d.TryGetProperty("png",out var png)){using var stream=new MemoryStream(Convert.FromBase64String(png.GetString()!));var old=image.Source as IDisposable;image.Source=new Bitmap(stream);old?.Dispose();}}
    public void ShowCapture(JsonElement d,LocalRow[] rows,bool healthy){SetImage(live,d);roster.Text=string.Join("\n",rows.Select(r=>$"{r.Name} · {r.Standing} · {r.Confidence:P0}"));status.Text=$"Checked {DateTime.Now:T} · {rows.Length} visible rows · "+(healthy?"stable":"confirming / unreadable")+" · visible roster only";}
    public void ShowError(string text)=>status.Text=text;
}
