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
 Directory.CreateDirectory(folder);var file=Path.Combine(folder,"TestIntel.txt");
 File.WriteAllText(file,"\r\nChannel Name: TestIntel\n\n\uFEFF"+Line("1DQ1-A 3 hostiles"),Encoding.Unicode);
 await PumpUntil(()=>Count()=="1 REPORTS","New default folder and UTF16 log ingested without configuration");
 File.AppendAllText(file,"\uFEFF"+Line("T5ZI-S clear"),Encoding.Unicode);
 await PumpUntil(()=>Count()=="2 REPORTS","Appended live report appears automatically");
 File.WriteAllText(Path.Combine(folder,"NewSession.txt"),Line("Jita hostile"));
 await PumpUntil(()=>Count()=="3 REPORTS","New channel/session file appears automatically");
 window.FindControl<TextBox>("IntelSearch")!.Text="hostile";
 await PumpUntil(()=>Count().Contains("2 MATCHES"),"Search filters retained intel");
 window.FindControl<TextBox>("IntelSearch")!.Text="";
 if(window.FindControl<Control>("ToggleWatch")!=null||window.FindControl<Control>("DemoButton")!=null)throw new Exception("Old controls remain");
 if(window.FindControl<TextBlock>("AlertTitle")!.Text!="MONITORING UNAVAILABLE")throw new Exception("No capture must never imply all clear");
 window.FindControl<ComboBox>("RouteMode")!.SelectedIndex=2;
 window.FindControl<AutoCompleteBox>("FromBox")!.Text="1DQ1-A";window.FindControl<AutoCompleteBox>("ToBox")!.Text="N-8YET";
 window.FindControl<Button>("PlanRoute")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
 await PumpUntil(()=>window.FindControl<TextBlock>("RouteSummary")!.Text!.Contains("LY total"),"Capital planner button produces a route");
 if(window.FindControl<StarMap>("Map")!.RouteLegs.Any(l=>l.LightYears>7))throw new Exception("Capital range exceeded");
 Directory.CreateDirectory("screenshots");
 await Task.Delay(300);
 using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v010-intel.png");
 if(!window.FindControl<TextBlock>("IntelDiagnostics")!.Text!.Contains("Last report:"))throw new Exception("Missing intel diagnostics");
 if(!window.FindControl<TextBlock>("IntelState")!.Text!.Contains("~/Documents/EVE/logs/"))throw new Exception("Default path is not displayed relative to current user");
 Console.WriteLine("PASS: Intel diagnostics and current-user default path visible");
 var admPicker=window.FindControl<ComboBox>("AdmFilterPicker")!;
 admPicker.SelectedIndex=1;if(window.FindControl<StarMap>("Map")!.AdmThreshold!=5)throw new Exception("ADM <5 selection failed");
 admPicker.SelectedIndex=2;if(window.FindControl<StarMap>("Map")!.AdmThreshold!=4 || SettingsStore.Load().AdmThreshold!=4)throw new Exception("ADM <4 persistence failed");
 Console.WriteLine("PASS: ADM threshold controls update map and persist preference");
 await Task.Delay(300);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v010-intel.png");
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
 var routeList=window.FindControl<ListBox>("RouteList")!;
 routeList.ItemsSource=new[]{new LiveItem("Jita","Jita")};routeList.SelectedIndex=0;
 routeList.RaiseEvent(new Avalonia.Input.KeyEventArgs{RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.Enter});
 if(window.FindControl<TextBlock>("RegionTitle")!.Text!="The Forge")throw new Exception("Explicit system activation failed");
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
 foreach(var name in new[]{"FromBox","ToBox"})
 {
     var box=window.FindControl<AutoCompleteBox>(name)!;
     var input=box.GetVisualDescendants().OfType<TextBox>().Single();
     input.Focus();input.SelectAll();window.KeyTextInput("p");
     await PumpUntil(()=>box.IsDropDownOpen,name+" suggests from the first character");
     window.KeyTextInput("-z");
     await PumpUntil(()=>box.IsDropDownOpen,name+" opens prefix suggestions");
     var matches=box.ItemsSource!.Cast<string>().Where(n=>box.TextFilter!("p-z",n)).ToArray();
     if(!matches.Contains("P-ZMZV") || matches.Any(n=>!n.StartsWith("p-z",StringComparison.OrdinalIgnoreCase)))
         throw new Exception("Incorrect autocomplete matches");
     window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowDown,Avalonia.Input.RawInputModifiers.None);
     window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter,Avalonia.Input.RawInputModifiers.None);await Task.Delay(100);
     if(box.Text!="P-ZMZV")throw new Exception("Suggestion did not fill route field");
     box.IsDropDownOpen=false;
 }
 Console.WriteLine("PASS: Start and destination suggest P-ZMZV for p-z and accept selection");
 await Task.Delay(300);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v010-intel.png");
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
 await Task.Delay(200);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v010-fixtures.png");
 window.Width=1280;window.Height=850;await Task.Delay(300);
 if(window.FindControl<Border>("LivePanel")!.Bounds.Height<280 || window.FindControl<ListBox>("IntelList")!.Bounds.Height<30 || connections.Bounds.Height<50)throw new Exception("Minimum-size panels unusable");
 using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v010-small.png");
 Console.WriteLine("PASS: Intel stream and connection table remain usable at 1280 x 850");
 window.Width=1560;window.Height=1000;
 live.Wormholes.Clear();live.Wormholes.AddRange(retained);search.Text=" ";
 await Task.Delay(300);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v010-live.png");
 Console.WriteLine("Automatic desktop ingestion smoke checks passed on "+System.Runtime.InteropServices.RuntimeInformation.OSDescription);
}
catch(Exception error) { failure=error; }
finally { window.Close();if(Directory.Exists(folder))Directory.Delete(folder,true);shutdown.Cancel(); }
});
Dispatcher.UIThread.MainLoop(shutdown.Token);
if(failure!=null)throw failure;
