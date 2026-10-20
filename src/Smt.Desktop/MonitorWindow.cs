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
    private readonly ClientMonitor? client;
    private readonly TextBox clientLabel=new(){Watermark="Character / client label"};
    private readonly ComboBox windows=new(){HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};
    private readonly Image preview=new(){Stretch=Stretch.Fill,Width=800,Height=450};
    private readonly Image live=new(){Stretch=Stretch.Uniform,Height=120};
    private readonly Canvas overlay=new(){Width=800,Height=450,Background=Brushes.Transparent};
    private readonly Border selection=new(){BorderBrush=Brushes.Cyan,BorderThickness=new Thickness(2),IsHitTestVisible=false};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock roster=new(){TextWrapping=TextWrapping.Wrap};
    private readonly ComboBox areaMode=new(){ItemsSource=new[]{"Local member list","System name (optional)"},SelectedIndex=0};
    private readonly AutoCompleteBox system=new(){Watermark="Current system (manual fallback)",MinimumPrefixLength=1,FilterMode=AutoCompleteFilterMode.StartsWithOrdinal};
    private readonly TextBlock constellation=new(){Name="AutomaticConstellation",TextWrapping=TextWrapping.Wrap};
    private readonly NumericUpDown minutes=new(){Minimum=1,Maximum=30,Value=5,FormatString="'Intel expiry minutes: '0"};
    private readonly CheckBox yellowVoice=new(){Name="YellowVoice",Content="Voice"},yellowSound=new(){Name="YellowSound",Content="Sound"},yellowPopup=new(){Name="YellowPopup",Content="Popup"};
    private readonly CheckBox redVoice=new(){Name="RedVoice",Content="Voice"},redSound=new(){Name="RedSound",Content="Sound"},redPopup=new(){Name="RedPopup",Content="Popup"};
    private readonly TextBox yellowMessage=new(){Name="YellowMessage",Watermark="Yellow alert message"},redMessage=new(){Name="RedMessage",Watermark="Red alert message"};
    private readonly Slider volume=new(){Minimum=0,Maximum=1,Value=.7};
    public sealed record VoiceChoice(string Id,string Label) {public override string ToString()=>Label;}
    private readonly ComboBox voicePicker=new(){Name="VoicePicker",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};
    private readonly TextBlock voiceStatus=new(){TextWrapping=TextWrapping.Wrap};
    private bool closed;
    private Point? drag;
    private double zoom=1,baseHeight=450;
    private readonly Grid captureGrid=new();
    private readonly ScrollViewer viewport=new(){Height=450,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};
    private double[]? localArea,systemArea;
    private int? previewId;
    public MonitorWindow(MainWindow owner,Universe universe,MonitorPrefs prefs,ClientMonitor? client=null)
    {
        this.owner=owner;this.client=client;clientLabel.Text=prefs.Label;Title="SMT4Mac · Local monitoring & alerts";Width=880;Height=880;MinWidth=850;Background=Brush.Parse("#0E1723");
        if((client==null?owner.CaptureSelection:client.Area==null?null:(client.WindowId,client.Area,client.SystemArea)) is {} previous)
        {
            var selected=new CaptureWindow(previous.Item1,"Previously selected EVE window");windows.ItemsSource=new[]{selected};windows.SelectedItem=selected;
            previewId=previous.Item1;localArea=previous.Item2;systemArea=previous.Item3;
            status.Text="Previous window and crop retained. Start monitoring to resume, or load a preview to adjust.";
        }
        system.ItemsSource=universe.Systems.Keys.Order().ToArray();system.Text=prefs.System;
        system.Name="ManualSystem";
        void Coverage(){var current=universe.Systems.GetValueOrDefault(system.Text?.Trim()??"");constellation.Text=current==null?"Enter a valid system to set nearby coverage.":$"Constellation: {current.Constellation}\nEntire constellation OR within 3 stargate jumps of {current.Name}.";}
        system.PropertyChanged+=(_,e)=>{if(e.Property==AutoCompleteBox.TextProperty)Coverage();};Coverage();
        minutes.Value=prefs.Minutes;volume.Value=prefs.Volume;
        voicePicker.ItemsSource=new[]{new VoiceChoice("","Automatic · prefer South African English"),new VoiceChoice(prefs.VoiceIdentifier,"Saved voice · checking availability")};
        voicePicker.SelectedIndex=string.IsNullOrEmpty(prefs.VoiceIdentifier)?0:1;
        Opened+=async(_,_)=>await RefreshVoices();
        yellowVoice.IsChecked=prefs.YellowAlert.Voice;yellowSound.IsChecked=prefs.YellowAlert.Sound;yellowPopup.IsChecked=prefs.YellowAlert.Popup;yellowMessage.Text=prefs.YellowAlert.Message;
        redVoice.IsChecked=prefs.RedAlert.Voice;redSound.IsChecked=prefs.RedAlert.Sound;redPopup.IsChecked=prefs.RedAlert.Popup;redMessage.Text=prefs.RedAlert.Message;
        Closed+=(_,_)=>{closed=true;(preview.Source as IDisposable)?.Dispose();(live.Source as IDisposable)?.Dispose();};
        var stack=new StackPanel{Spacing=10,Margin=new Thickness(20)};
        Content=new ScrollViewer{Content=stack,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        stack.Children.Add(new TextBlock{Text="LOCAL SCREEN MONITOR · BETA",FontSize=20,FontWeight=FontWeight.Bold});
        stack.Children.Add(new TextBlock{Text="Select an EVE window, load its preview, then drag around the Local member names and standing icons. Empty space below the list is allowed. The numeric member-count header is excluded automatically. Exclude chat text. Optionally select a separate area containing only your system name. Screenshots stay on this Mac; none are saved or uploaded.",TextWrapping=TextWrapping.Wrap});
        stack.Children.Add(clientLabel);stack.Children.Add(windows);
        var buttons=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8};stack.Children.Add(buttons);
        Button Add(string text,Action action){var b=new Button{Content=text};b.Click+=(_,_)=>action();buttons.Children.Add(b);return b;}
        Add("Find EVE windows",async()=>await Find());Add("Load preview",async()=>await Preview());Add("Auto-detect Local",async()=>await AutoDetect());Add("Start monitoring",()=>Start());Add("Stop capture",()=>{if(client==null)owner.StopMonitor();else client.Stop();status.Text="Capture stopped; intel ingestion continues.";});
        stack.Children.Add(areaMode);
        stack.Children.Add(new TextBlock{Text="Scroll over preview to zoom; use scrollbars to pan. Drag to select an area.",TextWrapping=TextWrapping.Wrap});
        var zoomBar=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8};
        foreach(var (label,factor) in new[]{("−",.8),("+",1.25),("Fit",0.0)}){var b=new Button{Content=label,Name=label=="+"?"CaptureZoomIn":label=="−"?"CaptureZoomOut":"CaptureFit"};b.Click+=(_,_)=>SetZoom(factor==0?1:zoom*factor);zoomBar.Children.Add(b);}
        stack.Children.Add(zoomBar);stack.Children.Add(status);
        captureGrid.Width=800;captureGrid.Height=450;captureGrid.Children.Add(preview);captureGrid.Children.Add(overlay);overlay.Children.Add(selection);viewport.Content=captureGrid;stack.Children.Add(viewport);
        overlay.PointerWheelChanged+=(_,e)=>{if(drag!=null)return;SetZoom(zoom*(e.Delta.Y>0?1.2:1/1.2),e.GetPosition(viewport));e.Handled=true;};
        areaMode.SelectionChanged+=(_,_)=>DrawArea();
        overlay.PointerPressed+=(_,e)=>{if(previewId==null)return;drag=e.GetPosition(overlay);e.Pointer.Capture(overlay);};
        overlay.PointerMoved+=(_,e)=>{if(drag is {} a){var p=e.GetPosition(overlay);Draw(a,p);}};
        overlay.PointerReleased+=(_,e)=>
        {
            if(drag is not {} a)return;var p=e.GetPosition(overlay);drag=null;e.Pointer.Capture(null);
            var roi=CaptureArea.Normalize(a.X,a.Y,p.X,p.Y,overlay.Width,overlay.Height);
            if(roi[2]*overlay.Width<10||roi[3]*overlay.Height<10){status.Text="Area too small; drag a larger rectangle.";return;}
            if(areaMode.SelectedIndex==0)localArea=roi;else systemArea=roi;
            status.Text=(areaMode.SelectedIndex==0?"Local":"System")+" area selected. Press Start monitoring to apply.";
        };
        stack.Children.Add(live);stack.Children.Add(roster);
        stack.Children.Add(new TextBlock{Text="Nullsec rules: green, blue and purple icons are friendly. Visible red, grey, white neutral, orange and yellow badges trigger Red after the same pilot is confirmed on two scans. Unbadged self rows and empty space are ignored; uncertain badges show a warning. Verify recognition against your EVE settings. Local automatically clears on a healthy scan with only friendly or unbadged rows. Keep the Local window and crop fixed; off-screen pilots cannot be detected.",TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#E8BE76")});
        stack.Children.Add(system);
        stack.Children.Add(new TextBlock{Text="AUTOMATIC YELLOW COVERAGE",FontWeight=FontWeight.Bold});stack.Children.Add(constellation);stack.Children.Add(minutes);
        void AlertSection(string level,string description,CheckBox voice,CheckBox sound,CheckBox popup,TextBox message)
        {
            stack.Children.Add(new TextBlock{Text=level.ToUpperInvariant()+" · "+description,FontWeight=FontWeight.Bold});
            var options=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=15};options.Children.Add(voice);options.Children.Add(sound);options.Children.Add(popup);stack.Children.Add(options);stack.Children.Add(message);
            var test=new Button{Content="Test "+level.ToLowerInvariant()+" alert"};test.Click+=async(_,_)=>await owner.TestAlert(level,Prefs());stack.Children.Add(test);
        }
        AlertSection("Yellow","Threats nearby",yellowVoice,yellowSound,yellowPopup,yellowMessage);
        AlertSection("Red","Immediate local threat",redVoice,redSound,redPopup,redMessage);
        stack.Children.Add(new TextBlock{Text=client==null?"Voice volume · all client alerts":"Preview volume · live alerts use primary client voice and volume"});stack.Children.Add(volume);
        stack.Children.Add(new TextBlock{Text="Alert voice · both alert levels",FontWeight=FontWeight.Bold});stack.Children.Add(voicePicker);stack.Children.Add(voiceStatus);
        var voiceButtons=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8};
        var refreshVoices=new Button{Content="Refresh voices"};refreshVoices.Click+=async(_,_)=>await RefreshVoices();voiceButtons.Children.Add(refreshVoices);
        var previewVoice=new Button{Name="PreviewVoice",Content="Preview voice"};previewVoice.Click+=async(_,_)=>{
            try{await NativeCapture.Call(new{action="alert",voice=true,siren=false,message="Caution, hostiles in the nearby area. Alert, hostiles entering local.",volume=volume.Value,voiceIdentifier=(voicePicker.SelectedItem as VoiceChoice)?.Id??""});}
            catch(Exception e){voiceStatus.Text="Voice preview unavailable: "+e.Message;}
        };voiceButtons.Children.Add(previewVoice);stack.Children.Add(voiceButtons);
        stack.Children.Add(new TextBlock{Text="Download additional voices in System Settings → Accessibility → Read & Speak (Spoken Content on older macOS) → System voice → Manage Voices, then refresh. Enhanced and Premium voices appear when available to apps. Siri-only voices may not appear. Preview works even when alert voice toggles are off.",TextWrapping=TextWrapping.Wrap});
        var save=new Button{Name="SaveAlerts",Content="Save alert settings"};save.Click+=(_,_)=>{SavePrefs();status.Text="Alert settings saved. No ESI login is used.";};stack.Children.Add(save);
        var clear=new Button{Content="Clear system capture area (use manual system)"};clear.Click+=(_,_)=>{systemArea=null;status.Text="System area cleared; press Start monitoring to apply.";};stack.Children.Add(clear);
    }
    private MonitorPrefs Prefs()=>new(System:system.Text?.Trim()??"",Coverage:"Automatic",Jumps:3,Minutes:(int)(minutes.Value??5),Label:clientLabel.Text?.Trim()??"EVE client",Volume:volume.Value,VoiceIdentifier:(voicePicker.SelectedItem as VoiceChoice)?.Id??"",
        Yellow:new(yellowVoice.IsChecked==true,yellowSound.IsChecked==true,yellowPopup.IsChecked==true,yellowMessage.Text??""),
        Red:new(redVoice.IsChecked==true,redSound.IsChecked==true,redPopup.IsChecked==true,redMessage.Text??""));
    private async Task RefreshVoices()
    {
        try{var data=await NativeCapture.Call(new{action="voices"});if(!closed)ApplyVoices(data);}
        catch(Exception e){if(!closed)voiceStatus.Text="Could not list installed voices: "+e.Message;}
    }
    public void ApplyVoices(JsonElement data)
    {
        var selected=(voicePicker.SelectedItem as VoiceChoice)?.Id??"";
        var choices=new List<VoiceChoice>{new("","Automatic · prefer South African English")};
        choices.AddRange(data.GetProperty("voices").EnumerateArray().Select(v=>new VoiceChoice(v.GetProperty("id").GetString()!, $"{v.GetProperty("name").GetString()} · {v.GetProperty("language").GetString()} · {v.GetProperty("quality").GetString()}")));
        var automatic=data.GetProperty("automaticVoiceId").GetString();
        var label=choices.FirstOrDefault(v=>v.Id==automatic && v.Id!="")?.Label??"system default";
        var missing=selected!=""&&!choices.Any(v=>v.Id==selected);
        if(missing)choices.Add(new(selected,"Saved voice unavailable · using automatic fallback"));
        voicePicker.ItemsSource=choices;voicePicker.SelectedItem=choices.First(v=>v.Id==selected);
        voiceStatus.Text=(missing?"Saved voice is unavailable. ":"")+"Automatic voice: "+label;
    }
    private void SavePrefs(){if(client==null)owner.ConfigureMonitor(Prefs());else owner.ConfigureClient(client,Prefs());}
    private async Task AutoDetect()
    {
        if(windows.SelectedItem is not CaptureWindow w){ShowError("Choose an EVE window first.");return;}
        try{localArea=null;systemArea=null;DrawArea();var d=await NativeCapture.Call(new{action="detect",id=w.Id});SetImage(preview,d);baseHeight=800.0*d.GetProperty("height").GetInt32()/d.GetProperty("width").GetInt32();SetZoom(1);previewId=w.Id;localArea=d.GetProperty("roi").EnumerateArray().Select(v=>v.GetDouble()).ToArray();systemArea=null;areaMode.SelectedIndex=0;DrawArea();status.Text="Suggested Local area — REVIEW the highlighted bounds, including empty space for new pilots. Adjust by dragging if needed; Start monitoring confirms the selection. Keep Local visible and its layout fixed.";}
        catch(Exception e){ShowError("Auto-detection: "+e.Message+" Load preview and select Local manually.");}
    }
    private void Draw(Point a,Point b){Canvas.SetLeft(selection,Math.Clamp(Math.Min(a.X,b.X),0,overlay.Width));Canvas.SetTop(selection,Math.Clamp(Math.Min(a.Y,b.Y),0,overlay.Height));selection.Width=Math.Min(overlay.Width,Math.Abs(a.X-b.X));selection.Height=Math.Min(overlay.Height,Math.Abs(a.Y-b.Y));}
    private void SetZoom(double value,Point? anchor=null)
    {
        var old=zoom;zoom=Math.Clamp(value,1,6);var point=anchor??new Point(0,0);var offset=viewport.Offset;
        preview.Width=overlay.Width=captureGrid.Width=800*zoom;
        preview.Height=overlay.Height=captureGrid.Height=baseHeight*zoom;
        viewport.Offset=new Vector(Math.Max(0,(offset.X+point.X)*zoom/old-point.X),Math.Max(0,(offset.Y+point.Y)*zoom/old-point.Y));DrawArea();
    }
    private void DrawArea()
    {
        var area=areaMode.SelectedIndex==0?localArea:systemArea;
        if(area==null){selection.Width=selection.Height=0;return;}
        Draw(new Point(area[0]*overlay.Width,area[1]*overlay.Height),new Point((area[0]+area[2])*overlay.Width,(area[1]+area[3])*overlay.Height));
    }
    private async Task Find(){try{var d=await NativeCapture.Call(new{action="list"});windows.ItemsSource=d.GetProperty("windows").EnumerateArray().Select(w=>new CaptureWindow(w.GetProperty("id").GetInt32(),w.GetProperty("title").GetString()!)).ToArray();windows.SelectedIndex=0;status.Text="Choose the character's window, then load its preview.";}catch(Exception e){ShowError(e.Message);}}
    private async Task Preview()
    {
        if(windows.SelectedItem is not CaptureWindow w){ShowError("Choose an EVE window first.");return;}
        try{var d=await NativeCapture.Call(new{action="capture",id=w.Id,preview=true,scan=false});SetImage(preview,d);baseHeight=800.0*d.GetProperty("height").GetInt32()/d.GetProperty("width").GetInt32();SetZoom(1);if(previewId!=w.Id){localArea=null;systemArea=null;}previewId=w.Id;DrawArea();status.Text="Drag around Local names plus standing icons.";}catch(Exception e){ShowError(e.Message);}
    }
    private void Start(){if(windows.SelectedItem is not CaptureWindow w||w.Id!=previewId||localArea==null){ShowError("Load this window's preview and select a Local area first.");return;}SavePrefs();if(owner.WindowInUse(w.Id,client)){ShowError("This EVE window already has an active monitor.");return;}if(client==null)owner.StartMonitor(w.Id,localArea,systemArea);else client.Start(w.Id,localArea,systemArea);status.Text="Monitoring selected window. Leave Local visible and reselect the area if its layout changes.";}
    private static void SetImage(Image image,JsonElement d){if(d.TryGetProperty("png",out var png)){using var stream=new MemoryStream(Convert.FromBase64String(png.GetString()!));var old=image.Source as IDisposable;image.Source=new Bitmap(stream);old?.Dispose();}}
    public void ShowCapture(JsonElement d,LocalRow[] rows,bool healthy){SetImage(live,d);roster.Text=string.Join("\n",rows.Select(r=>$"{r.Name} · {r.Standing} · {r.Confidence:P0}"));status.Text=$"Checked {DateTime.Now:T} · {rows.Length} visible rows · "+(healthy?"stable":"confirming / unreadable")+" · visible roster only";}
    public void ShowError(string text)=>status.Text=text;
}
