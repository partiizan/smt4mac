using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Smt.Core;

namespace Smt.Desktop;

public sealed record SearchItem(StarSystem System)
{
    public override string ToString() => $"{System.Name}  ·  {System.Region}";
}
public sealed record RouteItem(int Index, StarSystem System)
{
    public override string ToString() => $"{Index:00}   {System.Name}   {System.Security:0.0}";
}
public sealed record IntelItem(IntelReport Report)
{
    public string Summary => string.Join(" · ", Report.Systems) + (Report.Clear ? " / CLEAR REPORTED" : " / REPORT");
    public string Body => Report.Message;
    public string Meta => $"{Report.Time:HH:mm:ss} UTC · {Report.Speaker} · {Report.Source}";
}

public partial class MainWindow : Window
{
    private readonly SovereigntyClient sovereignty = new(Path.Combine(SettingsStore.Folder,"sovereignty.json"));
    private readonly CancellationTokenSource closing = new();
    private bool refreshingAdm;
    private readonly Universe universe;
    private readonly IntelParser parser;
    private readonly List<IntelReport> reports = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Settings settings;
    private LogTailer? tailer;
    private bool polling, paused, demo;
    private DateTimeOffset? lastLogRead, lastIntelMatch;
    private string lastReadSummary="No messages read yet.";
    private string? selected;
    private T Get<T>(string name) where T : Control => this.FindControl<T>(name)!;

    public MainWindow()
    {
        InitializeComponent();
        var dataPath=Path.Combine(AppContext.BaseDirectory,"data","universe.json");
        if(!File.Exists(dataPath)) dataPath=Path.Combine(AppContext.BaseDirectory,"..","Resources","data","universe.json");
        universe = new Universe(dataPath);
        parser = new IntelParser(universe);
        settings = SettingsStore.Load();
        Get<TextBlock>("DataSummary").Text = $"{universe.Systems.Count:N0} systems · {universe.Data.Regions.Length} regional maps · SMT {universe.Data.Commit[..7]}";
        var picker=Get<ComboBox>("RegionPicker"); picker.ItemsSource=universe.Data.Regions.OrderBy(r=>r.Name).ToArray();
        picker.SelectionChanged+=(_,_)=>ChangeRegion();
        picker.SelectedItem=universe.Data.Regions.FirstOrDefault(r=>r.Name==settings.Region) ?? universe.Data.Regions[0];
        Get<TextBox>("ChannelFilter").Text=settings.ChannelFilter;
        Get<CheckBox>("HighSecOnly").IsChecked=settings.HighSecOnly;
        tailer=new LogTailer(LogFolderLocator.Resolve(settings.LogFolder));
        Get<TextBox>("SearchBox").TextChanged+=(_,_)=>
        {
            var items=universe.Search(Get<TextBox>("SearchBox").Text ?? "").Select(s=>new SearchItem(s)).ToArray();
            Get<ListBox>("SearchResults").ItemsSource=items;
            Get<ListBox>("SearchResults").IsVisible=items.Length>0;
        };
        WireSystemActivation(Get<ListBox>("SearchResults"),item=>(item as SearchItem)?.System.Name);
        var systemNames=universe.Systems.Keys.OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).ToArray();
        Get<AutoCompleteBox>("FromBox").ItemsSource=systemNames;
        Get<AutoCompleteBox>("ToBox").ItemsSource=systemNames;
        Map.SystemSelected+=name=>SelectSystem(name);
        Get<Button>("FromSelected").Click+=(_,_)=> { if(selected!=null) Get<AutoCompleteBox>("FromBox").Text=selected; };
        Get<Button>("ToSelected").Click+=(_,_)=> { if(selected!=null) Get<AutoCompleteBox>("ToBox").Text=selected; };
        Get<Button>("PlanRoute").Click+=(_,_)=>Plan();
        Get<Button>("ClearRoute").Click+=(_,_)=> { Map.Route=[]; Map.RouteLegs=[]; Map.InvalidateVisual(); Get<ListBox>("RouteList").ItemsSource=null; Get<TextBlock>("RouteSummary").Text="Route cleared."; };
        WireSystemActivation(Get<ListBox>("RouteList"),item=>(item as LiveItem)?.System);
        Get<Button>("FitMap").Click+=(_,_)=>Map.Fit();
        Get<Button>("ZoomIn").Click+=(_,_)=>Map.Zoom(1.25);
        Get<Button>("ZoomOut").Click+=(_,_)=>Map.Zoom(.8);
        Get<Button>("ChooseLogs").Click+=async (_,_)=>await ChooseLogFolder();
        Get<Button>("AutoLogs").Click+=async (_,_)=>
        {
            settings=settings with {LogFolder=""};
            if(demo) ToggleDemo();
            tailer=new LogTailer(LogFolderLocator.Resolve());reports.Clear();RefreshIntel();
            paused=false;Get<Button>("ToggleWatch").Content="Pause";Save();UpdateWatchState();await PollLogs();
        };
        Get<Button>("ToggleWatch").Click+=(_,_)=> { paused=!paused; Get<Button>("ToggleWatch").Content=paused?"Resume":"Pause"; UpdateWatchState(); };
        Get<Button>("DemoButton").Click+=(_,_)=>ToggleDemo();
        Get<TextBox>("ChannelFilter").TextChanged+=(_,_)=>
        {
            settings=settings with {ChannelFilter=Get<TextBox>("ChannelFilter").Text ?? ""};
            if(!demo) { reports.Clear(); if(tailer!=null) tailer=new LogTailer(tailer.Folder); RefreshIntel(); }
            Save();
        };
        Get<Button>("AddIntel").Click+=(_,_)=>AddManual();
        Get<TextBox>("ManualIntel").KeyDown+=(_,e)=> { if(e.Key==Avalonia.Input.Key.Enter) AddManual(); };
        WireSystemActivation(Get<ListBox>("IntelList"),item=>(item as IntelItem)?.Report.Systems.FirstOrDefault());
        Get<CheckBox>("ShowAdm").IsCheckedChanged+=(_,_)=> { Map.ShowAdm=Get<CheckBox>("ShowAdm").IsChecked==true; Map.InvalidateVisual(); };
        Get<ComboBox>("AdmFilterPicker").ItemsSource=new[]{"ADM highlight: Off","ADM below 5.0","ADM below 4.0"};
        Get<ComboBox>("AdmFilterPicker").SelectedIndex=settings.AdmThreshold==5?1:settings.AdmThreshold==4?2:0;
        Map.AdmThreshold=settings.AdmThreshold;
        Get<ComboBox>("AdmFilterPicker").SelectionChanged+=(_,_)=>
        {
            Map.AdmThreshold=Get<ComboBox>("AdmFilterPicker").SelectedIndex switch {1=>5,2=>4,_=>(double?)null};
            settings=settings with{AdmThreshold=Map.AdmThreshold};Save();UpdateAdmFilter();Map.InvalidateVisual();
        };
        Get<ComboBox>("RegionPicker").SelectionChanged+=(_,_)=>UpdateAdmFilter();
        Get<Button>("RefreshAdm").Click+=async (_,_)=>await UpdateAdmAsync();
        Opened+=async (_,_)=> { await PollLogs(); await UpdateAdmAsync(); };
        timer.Tick+=async (_,_)=> { await PollLogs(); await UpdateAdmAsync(); }; timer.Start();
        ApplyAdm();
        InitializeOperations();
        Closed+=(_,_)=> { timer.Stop(); closing.Cancel(); sovereignty.Dispose(); StopOperations(); Save(); };
        UpdateWatchState(); SetStatus("Ready · Static gate map with public ESI sovereignty data.");
    }
    public async Task UpdateAdmAsync()
    {
        if(refreshingAdm || closing.IsCancellationRequested) return;
        refreshingAdm=true;
        Get<Button>("RefreshAdm").IsEnabled=false;
        try { await sovereignty.RefreshAsync(closing.Token); if(!closing.IsCancellationRequested) ApplyAdm(); }
        finally { refreshingAdm=false; if(!closing.IsCancellationRequested) Get<Button>("RefreshAdm").IsEnabled=true; }
    }
    private void ApplyAdm()
    {
        Map.Sovereignty=sovereignty.Systems; Map.AdmStale=sovereignty.IsStale; Map.InvalidateVisual();
        string checkedText=sovereignty.CheckedAt is {} t ? $"Checked {t:MMM d HH:mm} UTC" : "No ADM data yet";
        Get<TextBlock>("AdmStatus").Text=(sovereignty.Error!=null?"ESI unavailable · ":sovereignty.FromDisk?"Cached · ":"Public ESI · ")+checkedText+(sovereignty.IsStale && sovereignty.CheckedAt!=null?" · * cached/old values":"")+" · 5m refresh";
        ToolTip.SetTip(Get<TextBlock>("AdmStatus"),sovereignty.Error ?? "Activity Defense Multiplier from CCP ESI. N/A = no applicable sovereignty ADM; — = not reported. Refresh respects the server cache.");
        UpdateAdmDetails();UpdateAdmFilter();
    }
    private void UpdateAdmFilter()
    {
        if(Map.AdmThreshold is not {} threshold){Get<TextBlock>("AdmFilterSummary").Text="ADM highlighting off";return;}
        var region=Get<ComboBox>("RegionPicker").SelectedItem as Region;
        var count=region?.Nodes.Count(n=>!n.Outside && universe.Systems.TryGetValue(n.Name,out var s) && sovereignty.Systems.TryGetValue(s.Id,out var sov) && AdmFilter.Matches(sov.Adm,threshold))??0;
        Get<TextBlock>("AdmFilterSummary").Text=$"Gold rings · {count} systems below {threshold:0.0} in region"+(sovereignty.IsStale?" · cached/unverified data":"");
    }
    private void UpdateAdmDetails()
    {
        if(selected==null) { Get<TextBlock>("AdmDetails").Text="Select a system to see ADM."; return; }
        var sys=universe.Systems[selected];
        if(!sovereignty.Systems.TryGetValue(sys.Id,out var sov)) { Get<TextBlock>("AdmDetails").Text="ADM · unavailable / not reported"; return; }
        string number(int? n)=>n?.ToString() ?? "—";
        Get<TextBlock>("AdmDetails").Text=sov.Adm is {} adm
            ? $"ADM {adm:0.0}×"+(sovereignty.IsStale?" · cached/old":"")+$"\nMilitary {number(sov.Military)} · Industry {number(sov.Industrial)}\nStrategic {number(sov.Strategic)}"+(sov.Capital?" · Capital":"")
            : sov.Kind is "Faction" or "Unclaimed" ? "ADM · N/A (no applicable sov ADM)" : "ADM · not reported by ESI";
    }
    private void SetStatus(string text)=>Get<TextBlock>("Status").Text=text;
    private void Save()
    {
        try { SettingsStore.Save(settings); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { SetStatus("Settings could not be saved: "+e.Message); }
    }
    // Rebinding live rows is not a request to navigate. Only deliberate user activation is.
    private void WireSystemActivation(ListBox list,Func<object?,string?> systemName)
    {
        void Activate(){if(systemName(list.SelectedItem) is {} name)SelectSystem(name);}
        list.Tapped+=(_,e)=>
        {
            if(e.Source is Control source && (source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>()) is {} row)
            {list.SelectedItem=row.DataContext;Activate();}
        };
        list.KeyDown+=(_,e)=>{if(e.Key==Avalonia.Input.Key.Enter){Activate();e.Handled=true;}};
    }
    private void ChangeRegion()
    {
        if(Get<ComboBox>("RegionPicker").SelectedItem is not Region region) return;
        Map.SetRegion(universe,region);
        if(selected!=null && !region.Nodes.Any(n=>n.Name==selected))
        {
            selected=null;Map.Selected=null;
            Get<TextBlock>("SystemName").Text="Select a system";
            Get<TextBlock>("SystemInfo").Text="Click any node on the map.";
            UpdateAdmDetails();
        }
        Get<TextBlock>("RegionTitle").Text=region.Name;
        Get<TextBlock>("RegionSubtitle").Text=$"{region.Nodes.Count(n=>!n.Outside)} systems · Stargate network";
        settings=settings with {Region=region.Name}; Save();
    }
    public void SelectSystem(string name)
    {
        if(!universe.Systems.TryGetValue(name,out var sys)) return;
        if(Get<ComboBox>("RegionPicker").SelectedItem is not Region current || !current.Nodes.Any(n=>n.Name==sys.Name))
        {
            var region=universe.Data.Regions.FirstOrDefault(r=>r.Name==sys.Region) ?? universe.Data.Regions.FirstOrDefault(r=>r.Nodes.Any(n=>n.Name==sys.Name));
            if(region!=null) Get<ComboBox>("RegionPicker").SelectedItem=region;
        }
        selected=sys.Name; Map.Selected=sys.Name;
        Get<TextBlock>("SystemName").Text=sys.Name;
        Get<TextBlock>("SystemInfo").Text=$"{sys.Region}\nSecurity {sys.Security:0.00} · {sys.Jumps.Length} gates" + (sys.Station?"\nNPC station present":"");
        UpdateAdmDetails();
        UpdateOperationalDetails();
        Map.InvalidateVisual();
    }
    private async Task ChooseLogFolder()
    {
        try
        {
            var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {Title="Choose EVE's Chatlogs folder",AllowMultiple=false});
            var path=folders.FirstOrDefault()?.TryGetLocalPath(); if(path==null) return;
            foreach(var leaf in new[]{"chatlogs","Chatlogs","ChatLogs"}) if(Directory.Exists(Path.Combine(path,leaf))) { path=Path.Combine(path,leaf); break; }
            settings=settings with {LogFolder=path}; tailer=new LogTailer(path); reports.Clear();
            if(demo) ToggleDemo(); paused=false; Get<Button>("ToggleWatch").Content="Pause";
            Save(); UpdateWatchState(); await PollLogs();
        }
        catch(Exception e) { SetStatus("Could not open folder: "+e.Message); }
    }
    private static string DisplayLogPath(string path)
    {
        var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);
        return path.StartsWith(home+Path.DirectorySeparatorChar,StringComparison.Ordinal)?"~"+path[home.Length..]:path;
    }
    private void UpdateWatchState()
    {
        string mode=string.IsNullOrWhiteSpace(settings.LogFolder)?"Automatic · current user":"Custom folder";
        var path=tailer?.Folder??LogFolderLocator.Resolve(settings.LogFolder);
        var displayPath=DisplayLogPath(path);
        ToolTip.SetTip(Get<TextBlock>("IntelState"),path);
        Get<TextBlock>("IntelState").Text=demo?"Demo mode · live monitoring suspended":paused?"Paused · reports still expire":$"{mode} · scanning every 2 seconds\n{displayPath}";
    }
    private async Task PollLogs()
    {
        if(polling || paused || demo) { Map.InvalidateVisual(); return; }
        var resolved=LogFolderLocator.Resolve(settings.LogFolder);
        if(tailer==null || tailer.Folder!=resolved) tailer=new LogTailer(resolved);
        var current=tailer; var filter=settings.ChannelFilter; polling=true;
        try
        {
            var batch=await Task.Run(()=>
            {
                var lines=current.Poll();
                var filtered=lines.Where(x=>string.IsNullOrWhiteSpace(filter)||x.Source.Contains(filter,StringComparison.OrdinalIgnoreCase)).ToArray();
                var matches=filtered.Select(x=>parser.Parse(x.Line,x.Source,DateTimeOffset.UtcNow)).Where(r=>r!=null).Cast<IntelReport>().ToArray();
                var now=DateTimeOffset.UtcNow;
                var recent=matches.Where(r=>r.Time>=now.AddMinutes(-15) && r.Time<=now.AddMinutes(1)).ToArray();
                return (LineCount:lines.Count,Filtered:filtered.Length,Matches:matches.Length,Recent:recent);
            });
            if(current!=tailer || demo || paused || closing.IsCancellationRequested) return;
            foreach(var r in batch.Recent) AddReport(r);
            if(current.BytesRead>0)lastLogRead=DateTimeOffset.UtcNow;
            if(batch.Recent.Length>0)lastIntelMatch=DateTimeOffset.UtcNow;
            if(batch.LineCount>0)lastReadSummary=$"Last batch: {batch.LineCount} lines · {batch.Filtered} pass channel filter · {batch.Recent.Length} recent reports · {batch.Matches-batch.Recent.Length} outside time window.";
            RefreshIntel(); UpdateWatchState();
            var overview=current.FilesFound==0?"No .txt chat logs found in this folder.":current.EligibleFiles==0?$"{current.FilesFound} files found; none modified in the last 2 days.":$"{current.FilesRead}/{current.EligibleFiles} recent log files readable · {current.BytesRead:N0} new bytes this scan.";
            var errors=current.ReadErrors.Count>0?$"\n{current.ReadErrors.Count} file(s) could not be read; retrying. Check file access.":"";
            Get<TextBlock>("IntelDiagnostics").Text=overview+errors+"\n"+lastReadSummary+
                "\nLast data read: "+(lastLogRead is {} read?$"{read:HH:mm:ss} UTC":"never")+" · Last report: "+(lastIntelMatch is {} match?$"{match:HH:mm:ss} UTC":"none");
            ToolTip.SetTip(Get<TextBlock>("IntelDiagnostics"),current.ReadErrors.Count>0?string.Join("\n",current.ReadErrors):"Complete lines are parsed; only reports from the last 15 minutes are imported. Counts include headers and messages without system names.");
            SetStatus($"Intel checked {DateTime.Now:HH:mm:ss} · {batch.Recent.Length} recent matching reports this scan"+errors.Replace("\n"," "));

        }
        catch(DirectoryNotFoundException)
        { Get<TextBlock>("IntelDiagnostics").Text="Folder not found. Waiting for EVE to create it."; Get<TextBlock>("IntelState").Text="Waiting for EVE chat logs · checking every 2 seconds\n"+DisplayLogPath(current.Folder); SetStatus("Open EVE with chat logging enabled. Use Change folder only if your logs are stored elsewhere."); }
        catch(UnauthorizedAccessException)
        { Get<TextBlock>("IntelDiagnostics").Text="Cannot read the folder: Documents permission is needed."; Get<TextBlock>("IntelState").Text="Documents access needed · retrying\n"+DisplayLogPath(current.Folder); SetStatus("Allow SMT Mac Beta to read Documents when macOS asks, or grant Documents access in System Settings → Privacy & Security → Files and Folders."); }
        catch(Exception e) when(e is IOException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { Get<TextBlock>("IntelDiagnostics").Text="Read failed: "+e.Message; Get<TextBlock>("IntelState").Text="Log access error · retrying\n"+DisplayLogPath(current.Folder); SetStatus(e.Message); }
        finally { polling=false; }
    }
    private void AddReport(IntelReport report)
    {
        if(reports.Any(r=>r.Time==report.Time && r.Speaker==report.Speaker && r.Message==report.Message && r.Source==report.Source)) return;
        reports.Add(report); if(reports.Count>500) reports.RemoveRange(0,reports.Count-500);
    }
    private void RefreshIntel()
    {
        Get<ListBox>("IntelList").ItemsSource=reports.OrderByDescending(r=>r.Time).Take(100).Select(r=>new IntelItem(r)).ToArray();
        Get<TextBlock>("IntelCount").Text=$"{reports.Count} REPORTS";
        var latest=new Dictionary<string,IntelReport>();
        foreach(var r in reports.OrderBy(r=>r.Time)) foreach(var name in r.Systems) latest[name]=r;
        Map.Reports=latest; Map.InvalidateVisual();
    }
    private void AddManual()
    {
        var text=Get<TextBox>("ManualIntel").Text ?? "";
        var report=parser.Parse(text,demo?"Demo / manual":"Manual",DateTimeOffset.UtcNow,true);
        if(report==null) { SetStatus("No exact system name found in the report."); return; }
        AddReport(report); RefreshIntel(); Get<TextBox>("ManualIntel").Text="";
    }
    public void ToggleDemo()
    {
        demo=!demo; reports.Clear();
        Get<Border>("DemoBanner").IsVisible=demo; Get<Button>("DemoButton").Content=demo?"Exit demo":"Try demo";
        if(demo)
        {
            Get<ComboBox>("RegionPicker").SelectedItem=universe.Data.Regions.First(r=>r.Name=="Delve");
            var now=DateTimeOffset.UtcNow;
            foreach(var (msg,age) in new[]{("1DQ1-A 3 neutrals on the gate",1),("T5ZI-S clear",3),("N-8YET interceptor moving towards 1DQ1-A",5)})
            {
                var r=parser.Parse(msg,"DEMO",now.AddMinutes(-age),true); if(r!=null) AddReport(r with {Speaker="Demo scout"});
            }
            Get<AutoCompleteBox>("FromBox").Text="1DQ1-A"; Get<AutoCompleteBox>("ToBox").Text="N-8YET"; Get<CheckBox>("HighSecOnly").IsChecked=false; Plan();
        }
        else if(tailer!=null) tailer=new LogTailer(tailer.Folder);
        RefreshIntel(); UpdateWatchState(); SetStatus(demo?"Synthetic reports for demonstration only. No live intel is mixed into this view.":"Demo ended. Automatic live monitoring resumes.");
    }
}
