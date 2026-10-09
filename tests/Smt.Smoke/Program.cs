using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Smt.Desktop;

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }).UseSkia().WithInterFont().SetupWithoutStarting();
var window=new MainWindow {Width=1460,Height=900};
window.Show(); Dispatcher.UIThread.RunJobs();
window.ToggleDemo(); Dispatcher.UIThread.RunJobs();
var wait=System.Diagnostics.Stopwatch.StartNew();
while(window.FindControl<StarMap>("Map")!.AdmStale && wait.Elapsed<TimeSpan.FromSeconds(35))
 { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
if(window.FindControl<StarMap>("Map")!.Sovereignty.Count<5000 || window.FindControl<StarMap>("Map")!.AdmStale)
 throw new Exception("Live public ESI ADM fetch failed: "+window.FindControl<TextBlock>("AdmStatus")!.Text);
if(!window.FindControl<TextBlock>("AdmDetails")!.Text!.Contains("Military")) throw new Exception("Selected system ADM/index detail missing");
Console.WriteLine("Live ADM detail: "+window.FindControl<TextBlock>("AdmDetails")!.Text);

window.Measure(new Size(1460,900)); window.Arrange(new Rect(0,0,1460,900)); Dispatcher.UIThread.RunJobs();
if(window.FindControl<TextBlock>("RegionTitle")!.Text!="Delve") throw new Exception("Demo region failed");
if(!window.FindControl<TextBlock>("RouteSummary")!.Text!.Contains("jumps")) throw new Exception("Demo routing failed");
if(!window.FindControl<Border>("DemoBanner")!.IsVisible) throw new Exception("Demo label missing");
using(var bitmap=new RenderTargetBitmap(new PixelSize(1460,900),new Vector(96,96))) { bitmap.Render(window); bitmap.Save(args.Length>0?args[0]:"preview.png"); }
window.ToggleDemo();
if(window.FindControl<Border>("DemoBanner")!.IsVisible) throw new Exception("Demo state did not clear");
window.Close();
Console.WriteLine("Desktop smoke checks passed; rendered preview saved.");
