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
 window.ToggleDemo();window.ToggleDemo();
 await PumpUntil(()=>Count()=="3 REPORTS","Monitoring resumes after demo with no duplicated history");
 window.FindControl<ComboBox>("RouteMode")!.SelectedIndex=2;
 window.FindControl<AutoCompleteBox>("FromBox")!.Text="1DQ1-A";window.FindControl<AutoCompleteBox>("ToBox")!.Text="N-8YET";
 window.FindControl<Button>("PlanRoute")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
 await PumpUntil(()=>window.FindControl<TextBlock>("RouteSummary")!.Text!.Contains("LY total"),"Capital planner button produces a route");
 if(window.FindControl<StarMap>("Map")!.RouteLegs.Any(l=>l.LightYears>7))throw new Exception("Capital range exceeded");
 Directory.CreateDirectory("screenshots");
 await Task.Delay(300);
 using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v08-intel.png");
 for(int tab=1;tab<=1;tab++){window.FindControl<TabControl>("OperationsTabs")!.SelectedIndex=tab;await Task.Delay(300);using var frame=window.CaptureRenderedFrame();frame?.Save($"screenshots/v08-tab{tab}.png");}
 if(!window.FindControl<TextBlock>("IntelDiagnostics")!.Text!.Contains("Last report:"))throw new Exception("Missing intel diagnostics");
 if(!window.FindControl<TextBlock>("IntelState")!.Text!.Contains("~/Documents/EVE/logs/"))throw new Exception("Default path is not displayed relative to current user");
 Console.WriteLine("PASS: Intel diagnostics and current-user default path visible");
 var admPicker=window.FindControl<ComboBox>("AdmFilterPicker")!;
 admPicker.SelectedIndex=1;if(window.FindControl<StarMap>("Map")!.AdmThreshold!=5)throw new Exception("ADM <5 selection failed");
 admPicker.SelectedIndex=2;if(window.FindControl<StarMap>("Map")!.AdmThreshold!=4 || SettingsStore.Load().AdmThreshold!=4)throw new Exception("ADM <4 persistence failed");
 Console.WriteLine("PASS: ADM threshold controls update map and persist preference");
 window.FindControl<TabControl>("OperationsTabs")!.SelectedIndex=0;
 await Task.Delay(300);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v08-intel.png");
 var tabs=window.FindControl<TabControl>("OperationsTabs")!;
 if(tabs.ItemCount!=2 || window.FindControl<Control>("LoginCharacter")!=null ||
    typeof(Universe).Assembly.GetType("Smt.Core.CharacterService")!=null)
     throw new Exception("Pilot/SSO removal incomplete");
 Console.WriteLine("PASS: Only Intel and Live tabs; SSO implementation absent");
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
 await Task.Delay(300);using(var frame=window.CaptureRenderedFrame())frame?.Save("screenshots/v08-intel.png");
 Console.WriteLine("Automatic desktop ingestion smoke checks passed on "+System.Runtime.InteropServices.RuntimeInformation.OSDescription);
}
catch(Exception error) { failure=error; }
finally { window.Close();if(Directory.Exists(folder))Directory.Delete(folder,true);shutdown.Cancel(); }
});
Dispatcher.UIThread.MainLoop(shutdown.Token);
if(failure!=null)throw failure;
