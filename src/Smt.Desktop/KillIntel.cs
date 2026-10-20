using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Smt.Core;
namespace Smt.Desktop;
public partial class MainWindow
{
    private readonly LiveApi killApi=new();
    private RegionalKills regionalKills=null!;
    private bool regionalBusy;
    private readonly Dictionary<long,string> killNames=[];
    private DateTimeOffset nextNames;
    private void InitializeKillIntel()
    {
        regionalKills=new(killApi,universe);
        Get<CheckBox>("ShowIntelKills").IsChecked=true;
        Get<CheckBox>("ShowIntelKills").IsCheckedChanged+=(_,_)=>RefreshIntel();
        Get<ComboBox>("RegionPicker").SelectionChanged+=(_,_)=>{RefreshIntel();_=RefreshRegionalKills();};
        Opened+=(_,_)=>_=RefreshRegionalKills();
    }
    private string CurrentRegion=>(Get<ComboBox>("RegionPicker").SelectedItem as Region)?.Name??"";
    private Kill[] CurrentKills()=>regionalKills==null||situation==null?[]:RegionalKills.Merge(regionalKills.For(CurrentRegion),situation.Kills,universe,CurrentRegion,DateTimeOffset.UtcNow);
    private async Task RefreshRegionalKills()
    {
        if(regionalBusy||closing.IsCancellationRequested||regionalKills==null||CurrentRegion=="")return;
        regionalBusy=true;var region=CurrentRegion;
        try{
            await regionalKills.Refresh(region,closing.Token);
            if(closing.IsCancellationRequested)return;
            RefreshIntel();
            if(DateTimeOffset.UtcNow>=nextNames){
                var ids=CurrentKills().SelectMany(k=>new[]{k.VictimId,k.AllianceId,k.CorporationId}).Where(id=>id>0&&!killNames.ContainsKey(id)).Distinct().Take(1000).ToArray();
                if(ids.Length>0){nextNames=DateTimeOffset.UtcNow.AddMinutes(2);try{var names=await killApi.JsonPost("https://esi.evetech.net/latest/universe/names/",ids,closing.Token);foreach(var n in names.EnumerateArray())killNames[n.Number("id")]=n.Str("name");}catch(Exception e) when(e is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException){ }}
            }
            if(!closing.IsCancellationRequested)RefreshIntel();
        }finally{regionalBusy=false;}
        if(region!=CurrentRegion&&!closing.IsCancellationRequested)_=RefreshRegionalKills();
    }
    private string KillDescription(Kill k)=>$"{ships.GetValueOrDefault(k.ShipType,$"Type {k.ShipType}")} · {k.Value:N0} ISK\n{(k.VictimId>0?killNames.GetValueOrDefault(k.VictimId,$"Pilot {k.VictimId}"):"Unpiloted / unknown victim")} · {(k.AllianceId>0?killNames.GetValueOrDefault(k.AllianceId,$"Alliance {k.AllianceId}"):k.CorporationId>0?killNames.GetValueOrDefault(k.CorporationId,$"Corporation {k.CorporationId}"):"No affiliation reported")}";
    private Control KillView(Kill k)
    {
        var system=universe.ById[k.SystemId].Name;
        var panel=new StackPanel{Spacing=5,Margin=new Thickness(0,6)};
        panel.Children.Add(new TextBlock{Text="KILL · "+system,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#F4AE87")});
        panel.Children.Add(new TextBlock{Text=KillDescription(k),FontSize=12,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text=$"{k.Time:MMM d HH:mm:ss} UTC · zKillboard · historical report",FontSize=10,TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#92A3B8")});
        var open=new Button{Content="Open kill report ↗",FontSize=10};open.Click+=(_,e)=>{Browse($"https://zkillboard.com/kill/{k.Id}/");e.Handled=true;};panel.Children.Add(open);return panel;
    }
}
