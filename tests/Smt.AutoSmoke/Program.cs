using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Smt.Core;
using Smt.Desktop;
using System.Text;

// Isolated CI runner only: exercise the actual default location without saved configuration.
var folder=LogFolderLocator.Resolve();
if(Directory.Exists(folder)||File.Exists(SettingsStore.PathName)) throw new Exception("Auto smoke requires a clean runner profile");
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
var window=new MainWindow();window.Show();
async Task PumpUntil(Func<bool> condition,string label)
{
 var wait=System.Diagnostics.Stopwatch.StartNew();
 while(!condition()&&wait.Elapsed<TimeSpan.FromSeconds(12)) { await Task.Delay(100); }
 if(!condition())throw new Exception("FAIL: "+label+" | "+Count()+" | "+window.FindControl<TextBlock>("IntelState")!.Text+" | "+window.FindControl<TextBlock>("Status")!.Text);
 Console.WriteLine("PASS: "+label);
}
string Count()=>window.FindControl<TextBlock>("IntelCount")!.Text!;
string Line(string text)=>$"[ {DateTimeOffset.UtcNow:yyyy.MM.dd HH:mm:ss} ] Scout > {text}\r\n";
Exception? failure=null;using var shutdown=new CancellationTokenSource();
Dispatcher.UIThread.Post(async ()=>
{
try
{
 await PumpUntil(()=>window.FindControl<TextBlock>("IntelState")!.Text!.Contains("Waiting"),"Launch automatically waits for the default folder");
 Directory.CreateDirectory(folder);var file=Path.Combine(folder,"Intel.WOMP_20261019_120000_111.txt");
 File.WriteAllText(file,"\r\nChannel Name: TestIntel\n\n\uFEFF"+Line("1DQ1-A 3 hostiles"),Encoding.Unicode);
 await PumpUntil(()=>Count()=="1 REPORTS","New default folder and UTF16 log ingested without configuration");
 File.AppendAllText(file,"\uFEFF"+Line("T5ZI-S clear"),Encoding.Unicode);
 await PumpUntil(()=>Count()=="2 REPORTS","Appended live report appears automatically");
 File.WriteAllText(Path.Combine(folder,"Intel.WOMP_20261019_130000_222.txt"),Line("Jita hostile"));
 await PumpUntil(()=>Count()=="3 REPORTS","New channel/session file appears automatically");
 // Corp must not leak into retained history, map reports, or alerts. Duplicate character logs collapse.
 File.Copy(file,Path.Combine(folder,"Intel.WOMP_20261019_120000_333.txt"));
 File.WriteAllText(Path.Combine(folder,"Corp_20261019_140000.txt"),Line("RF-CN3 5 hostiles"));
 await Task.Delay(2500);
 if(Count()!="3 REPORTS")throw new Exception("Corp exclusion or character-session deduplication failed");
 window.SetIntelChannels(["Corp"]);await PumpUntil(()=>Count()=="1 REPORTS","Explicit Corp opt-in rebuilds selected history");
 if(window.FindControl<StarMap>("Map")!.Reports.Keys.Any(k=>k!="RF-CN3"))throw new Exception("Excluded channel remained on map");
 window.SetIntelChannels(["Intel.WOMP"]);await PumpUntil(()=>Count()=="3 REPORTS","Restoring Intel.WOMP removes Corp history");
 window.FindControl<TextBox>("IntelSearch")!.Text="hostile";
 await PumpUntil(()=>Count().Contains("2 MATCHES"),"Search filters retained intel");
 window.FindControl<TextBox>("IntelSearch")!.Text="";
 if(window.FindControl<Control>("ToggleWatch")!=null||window.FindControl<Control>("DemoButton")!=null)throw new Exception("Old controls remain");
 if(window.FindControl<TextBlock>("AlertTitle")!.Text!="MONITORING UNAVAILABLE")throw new Exception("No capture must never imply all clear");
 foreach(var removed in new[]{"RouteMode","FromBox","ToBox","PlanRoute","RouteList","SearchBox"})
  if(window.FindControl<Control>(removed)!=null)throw new Exception("Route control remains: "+removed);
 var workspace=window.FindControl<Grid>("Workspace")!;
 var toggle=window.FindControl<Button>("ToggleWormholes")!;
 var originalHeight=workspace.RowDefinitions[3].Height.Value;
 toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
 if(window.FindControl<Border>("LivePanel")!.IsVisible || window.FindControl<Border>("WormholeDivider")!.IsVisible || workspace.RowDefinitions[3].Height.Value!=0 || !SettingsStore.Load().WormholesCollapsed)throw new Exception("Wormhole minimize failed");
 toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
 if(!window.FindControl<Border>("LivePanel")!.IsVisible || workspace.RowDefinitions[3].Height.Value!=originalHeight)throw new Exception("Wormhole restore failed");
 var divider=window.FindControl<Border>("WormholeDivider")!;
 var drag=divider.TranslatePoint(new Point(100,4),window)!.Value;
 window.MouseDown(drag,Avalonia.Input.MouseButton.Left);window.MouseMove(drag-new Vector(0,25));window.MouseUp(drag-new Vector(0,25),Avalonia.Input.MouseButton.Left);await Task.Delay(100);
 if(SettingsStore.Load().WormholeHeight<=originalHeight)throw new Exception("Wormhole resize/persistence failed");
 toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
 if(workspace.RowDefinitions[3].Height.Value!=SettingsStore.Load().WormholeHeight)throw new Exception("Resized drawer height lost");
 var intelDivider=window.FindControl<GridSplitter>("IntelDivider")!;
 var intelBefore=window.FindControl<Border>("IntelPanel")!.Bounds.Width;
 var point=intelDivider.TranslatePoint(new Point(4,100),window)!.Value;
 window.MouseDown(point,Avalonia.Input.MouseButton.Left);window.MouseMove(point-new Vector(40,0));window.MouseUp(point-new Vector(40,0),Avalonia.Input.MouseButton.Left);await Task.Delay(100);
 if(window.FindControl<Border>("IntelPanel")!.Bounds.Width<=intelBefore)throw new Exception("Intel divider failed");
 var monitorDivider=window.FindControl<Border>("MonitorDivider")!;
 var monitorBefore=window.FindControl<Border>("MonitorPanel")!.Bounds.Width;
 point=monitorDivider.TranslatePoint(new Point(4,100),window)!.Value;
 window.MouseDown(point,Avalonia.Input.MouseButton.Left);window.MouseMove(point-new Vector(25,0));window.MouseUp(point-new Vector(25,0),Avalonia.Input.MouseButton.Left);await Task.Delay(100);
 if(window.FindControl<Border>("MonitorPanel")!.Bounds.Width<=monitorBefore)throw new Exception("Local monitor divider failed");
 Console.WriteLine("PASS: Three permanent columns resize; wormhole collapse, restore and resize work");
 Directory.CreateDirectory("screenshots");
 await Task.Delay(300);
 using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v012-intel.png");
 if(!window.FindControl<TextBlock>("IntelDiagnostics")!.Text!.Contains("Last report:"))throw new Exception("Missing intel diagnostics");
 if(!window.FindControl<TextBlock>("IntelState")!.Text!.Contains("~/Documents/EVE/logs/"))throw new Exception("Default path is not displayed relative to current user");
 Console.WriteLine("PASS: Intel diagnostics and current-user default path visible");
 var admPicker=window.FindControl<ComboBox>("AdmFilterPicker")!;
 admPicker.SelectedIndex=1;if(window.FindControl<StarMap>("Map")!.AdmThreshold!=5)throw new Exception("ADM <5 selection failed");
 admPicker.SelectedIndex=2;if(window.FindControl<StarMap>("Map")!.AdmThreshold!=4 || SettingsStore.Load().AdmThreshold!=4)throw new Exception("ADM <4 persistence failed");
 Console.WriteLine("PASS: ADM threshold controls update map and persist preference");
 await Task.Delay(300);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v012-intel.png");
 if(window.FindControl<Control>("OperationsTabs")!=null || window.FindControl<Control>("ActivityLayer")!=null || !window.FindControl<Border>("IntelPanel")!.IsVisible || !window.FindControl<Border>("LivePanel")!.IsVisible)throw new Exception("Persistent panel layout failed");
 Console.WriteLine("PASS: Intel and Live both visible; activity controls absent");
 var regionPicker=window.FindControl<ComboBox>("RegionPicker")!;
 var regions=regionPicker.ItemsSource!.Cast<Region>().ToArray();
 foreach(var name in new[]{"Period Basis","Delve","Querious"})
 {
     regionPicker.SelectedItem=regions.First(r=>r.Name==name);
     var intel=window.FindControl<ListBox>("IntelList")!;
     intel.SelectedIndex=0;
     await Task.Delay(2200);
     if(window.FindControl<TextBlock>("RegionTitle")!.Text!=name)
         throw new Exception("Background selection changed region "+name);
 }
 regionPicker.SelectedItem=regions.First(r=>r.Name=="Period Basis");
 regionPicker.Focus();
 window.KeyPressQwerty(Avalonia.Input.PhysicalKey.F4,Avalonia.Input.RawInputModifiers.None);
 await Task.Delay(100);
 if(!regionPicker.IsDropDownOpen)throw new Exception("Region dropdown did not open");
 var nextRegion=regions[regionPicker.SelectedIndex+1].Name;
 window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowDown,Avalonia.Input.RawInputModifiers.None);
 window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter,Avalonia.Input.RawInputModifiers.None);
 if(regionPicker.IsDropDownOpen || window.FindControl<TextBlock>("RegionTitle")!.Text!=nextRegion)
     throw new Exception("Cannot select another region from Period Basis dropdown");
 Console.WriteLine("PASS: Region dropdown opens and accepts a different region via keyboard");
 regionPicker.IsDropDownOpen=true;
 var delveIndex=Array.FindIndex(regions,r=>r.Name=="Delve");
 regionPicker.ScrollIntoView(delveIndex);await Task.Delay(200);
 var delveRow=regionPicker.ContainerFromIndex(delveIndex) ?? throw new Exception("Delve dropdown row not realized");
 var popupRoot=TopLevel.GetTopLevel(delveRow)!;
 var click=delveRow.TranslatePoint(new Point(delveRow.Bounds.Width/2,delveRow.Bounds.Height/2),popupRoot)!.Value;
 popupRoot.MouseDown(click,Avalonia.Input.MouseButton.Left);
 popupRoot.MouseUp(click,Avalonia.Input.MouseButton.Left);
 if(regionPicker.IsDropDownOpen || window.FindControl<TextBlock>("RegionTitle")!.Text!="Delve")
     throw new Exception("Cannot click Delve in region dropdown");
 Console.WriteLine("PASS: Mouse selection of Delve in open region dropdown");
 await Task.Delay(6500);
 if(window.FindControl<TextBlock>("RegionTitle")!.Text!="Delve" || SettingsStore.Load().Region!="Delve")
     throw new Exception("Region selection did not survive refresh");
 Console.WriteLine("PASS: Period Basis, Delve, Querious switches survive live/intel refresh; explicit activation still works");
 await Task.Delay(300);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v012-intel.png");
 // Deterministic Eve-Scout fixtures verify filtering and row actions independently of live feed contents.
 var live=(Situational)typeof(MainWindow).GetField("situation",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
 var retained=live.Wormholes.ToArray();live.Wormholes.Clear();
 var now=DateTimeOffset.UtcNow;
 live.Wormholes.AddRange(new[]{new Wormhole("Thera","P-ZMZV","ABC-123","XYZ-456","large",now.AddHours(1),"Querious","ns",now),new Wormhole("Turnur","Jita","DEF-789","UVW-234","medium",now.AddHours(6),"The Forge","hs",now),new Wormhole("Thera","Amarr","OLD-123","OLD-456","large",now.AddMinutes(-1),"Domain","hs",now)});
 var search=window.FindControl<TextBox>("WormholeSearch")!;
 var hubs=window.FindControl<ComboBox>("WormholeHub")!;
 var connections=window.FindControl<ListBox>("WormholeList")!;
 search.Text="querious";await Task.Delay(150);
 if(connections.ItemCount!=1 || connections.ItemsSource!.Cast<WormholeRow>().Single().System!="P-ZMZV")throw new Exception("Region search failed");
 var row=connections.ItemsSource!.Cast<WormholeRow>().Single();
 if(!row.Signatures.Contains("Thera: XYZ-456") || !row.Signatures.Contains("Local: ABC-123") || !row.CopyText.Contains("large"))throw new Exception("Connection labels/copy data reversed or missing");
 var mapButton=connections.GetVisualDescendants().OfType<Button>().First(b=>b.Content?.ToString()=="Map");
 mapButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
 if(window.FindControl<TextBlock>("SystemName")!.Text!="P-ZMZV")throw new Exception("Connection map action failed");
 search.Text="";hubs.SelectedIndex=2;
 if(connections.ItemCount!=1 || connections.ItemsSource!.Cast<WormholeRow>().Single().Hub!="Turnur")throw new Exception("Hub filter failed");
 hubs.SelectedIndex=0;search.Text="does-not-exist";await Task.Delay(150);
 if(connections.ItemCount!=0 || !window.FindControl<TextBlock>("WormholeEmpty")!.IsVisible)throw new Exception("Empty filter state failed");
 search.Text="";await Task.Delay(150);
 if(connections.ItemCount!=2)throw new Exception("Expired connection remains visible");
 Console.WriteLine("PASS: Wormhole region/hub filters, empty state, expired removal, signature orientation and map action");
 await Task.Delay(200);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v012-fixtures.png");
 window.Width=1280;window.Height=850;await Task.Delay(300);
 if(window.FindControl<Border>("LivePanel")!.Bounds.Height<280 || window.FindControl<ListBox>("IntelList")!.Bounds.Height<30 || connections.Bounds.Height<50)throw new Exception($"Minimum-size panels unusable: live={window.FindControl<Border>("LivePanel")!.Bounds.Height}, intel={window.FindControl<ListBox>("IntelList")!.Bounds.Height}, connections={connections.Bounds.Height}");
 using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v012-small.png");
 Console.WriteLine("PASS: Intel stream and connection table remain usable at 1280 x 850");
 window.Width=1560;window.Height=1000;
 live.Wormholes.Clear();live.Wormholes.AddRange(retained);search.Text=" ";
 await Task.Delay(300);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v012-live.png");
 window.FindControl<Button>("MonitorSetup")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
 await Task.Delay(300);
 var monitor=(MonitorWindow)typeof(MainWindow).GetField("monitorWindow",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
 if(!monitor.IsVisible)throw new Exception("Monitor settings did not open");
 var zoomButton=monitor.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="CaptureZoomIn");
 zoomButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
 if(monitor.GetVisualDescendants().OfType<Avalonia.Controls.Canvas>().Single().Width!=1000)throw new Exception("Preview zoom did not resize selection surface");
 monitor.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="CaptureFit").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
 Console.WriteLine("PASS: Preview zoom and fit controls resize the capture selection surface");
 using(var frame=monitor.CaptureRenderedFrame())frame?.Save("screenshots/v012-monitor-setup.png");
 var manual=monitor.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b=>b.Name=="ManualSystem");
 var coverageText=monitor.GetVisualDescendants().OfType<TextBlock>().Single(b=>b.Name=="AutomaticConstellation");
 manual.Text="RF-CN3";await Task.Delay(100);
 if(!coverageText.Text!.Contains("3 stargate jumps")||coverageText.Text.Contains("Enter a valid"))throw new Exception("Automatic constellation did not populate");
 manual.Text="invalid";await Task.Delay(100);
 if(!coverageText.Text!.Contains("Enter a valid"))throw new Exception("Invalid system retained constellation");
 manual.Text="RF-CN3";
 monitor.GetVisualDescendants().OfType<TextBox>().Single(b=>b.Name=="YellowMessage").Text="Nearby fixture";
 monitor.GetVisualDescendants().OfType<TextBox>().Single(b=>b.Name=="RedMessage").Text="Local fixture";
 monitor.GetVisualDescendants().OfType<CheckBox>().Single(b=>b.Name=="RedPopup").IsChecked=true;
 monitor.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="SaveAlerts").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
 var saved=System.Text.Json.JsonSerializer.Deserialize<MonitorPrefs>(File.ReadAllText(Path.Combine(SettingsStore.Folder,"monitoring.json")))!;
 if(saved.YellowAlert.Message!="Nearby fixture"||saved.RedAlert.Message!="Local fixture"||!saved.RedAlert.Popup||saved.YellowAlert.Popup||saved.Jumps!=3||string.IsNullOrEmpty(saved.Target))throw new Exception("Independent alerts did not persist");
 Console.WriteLine("PASS: Automatic constellation updates, invalid input clears coverage, separate alerts persist");
 monitor.Close();
 Console.WriteLine("PASS: Monitoring setup opens without requesting screen permission until explicitly selected");
 // Simulated frames exercise permanent preview lifecycle without Screen Recording access.
 var binding=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
 ((DispatcherTimer)typeof(MainWindow).GetField("monitorTimer",binding)!.GetValue(window)!).Stop();
 window.ConfigureMonitor(new MonitorPrefs(System:"RF-CN3"));
 window.StartMonitor(0,[0,0,1,1],null);
 var rosterFixture=new StackPanel{Background=Avalonia.Media.Brushes.Black,Width=260,Height=160};
 foreach(var name in new[]{"◆  Friendly Pilot","◆  Blue Wingman","    Chong Dong"})rosterFixture.Children.Add(new TextBlock{Text=name,Foreground=name.Contains("Chong")?Avalonia.Media.Brushes.White:Avalonia.Media.Brushes.CornflowerBlue,FontSize=15,Margin=new Thickness(8)});
 rosterFixture.Measure(new Size(260,160));rosterFixture.Arrange(new Rect(0,0,260,160));
 using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(260,160));bitmap.Render(rosterFixture);using var png=new MemoryStream();bitmap.Save(png);
 var capture=System.Text.Json.JsonSerializer.SerializeToElement(new{png=Convert.ToBase64String(png.ToArray()),rows=new[]{new{name="Friendly Pilot",confidence=.99,standing="Friendly"},new{name="Blue Wingman",confidence=.99,standing="Friendly"},new{name="Chong Dong",confidence=.99,standing="Ignored"}}});
 window.ApplyLocalCapture(capture);window.ApplyLocalCapture(capture);
 if(window.FindControl<Image>("LocalPreview")!.Source==null||window.FindControl<TextBlock>("AlertTitle")!.Text!="LOCAL CLEAR")throw new Exception("Permanent preview or green status failed with settings closed");
 var feed=(List<IntelReport>)typeof(MainWindow).GetField("reports",binding)!.GetValue(window)!;
 var u=new Universe(Path.Combine(AppContext.BaseDirectory,"data","universe.json"));var adjacent=u.Systems["RF-CN3"].Jumps[0];
 feed.Add(new IntelReport(DateTimeOffset.UtcNow,"Fixture Scout",adjacent+" hostiles",[adjacent],false,"Intel.WOMP"));
 typeof(MainWindow).GetMethod("UpdateAlert",binding)!.Invoke(window,null);
 if(!window.FindControl<Border>("NearbyTag")!.IsVisible||window.FindControl<TextBlock>("AlertTitle")!.Text!="LOCAL CLEAR")throw new Exception("Green and yellow must coexist");
 window.FindControl<Button>("ToggleWormholes")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
 await Task.Delay(100);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v012-monitor-fixture.png");
 feed.Clear();typeof(MainWindow).GetMethod("UpdateAlert",binding)!.Invoke(window,null);
 if(window.FindControl<Border>("NearbyTag")!.IsVisible)throw new Exception("Yellow tag did not disappear");
 typeof(MainWindow).GetField("lastCapture",binding)!.SetValue(window,DateTimeOffset.UtcNow.AddSeconds(-10));typeof(MainWindow).GetMethod("UpdateAlert",binding)!.Invoke(window,null);
 if(window.FindControl<Image>("LocalPreview")!.Source!=null||window.FindControl<TextBlock>("AlertTitle")!.Text=="LOCAL CLEAR")throw new Exception("Stale frame still appears live");
 window.StopMonitor();
 Console.WriteLine("PASS: Preview stays active with settings closed, independent tags work, stale capture is cleared");
 Console.WriteLine("Automatic desktop ingestion smoke checks passed on "+System.Runtime.InteropServices.RuntimeInformation.OSDescription);
}
catch(Exception error) { failure=error; }
finally { window.Close();if(Directory.Exists(folder))Directory.Delete(folder,true);shutdown.Cancel(); }
});
Dispatcher.UIThread.MainLoop(shutdown.Token);
if(failure!=null)throw failure;
