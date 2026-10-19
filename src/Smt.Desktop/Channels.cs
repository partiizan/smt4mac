using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Smt.Core;
namespace Smt.Desktop;
public partial class MainWindow
{
    private void UpdateChannelSummary() => Get<TextBlock>("ChannelSummary").Text="Monitoring: "+(settings.IntelChannels is {Length:>0}?string.Join(", ",settings.IntelChannels):"no channels selected");
    public void SetIntelChannels(IEnumerable<string> channels)
    {
        settings=settings with{IntelChannels=channels.Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),ChannelFilter=""};
        reports.Clear();logsHealthy=false;if(tailer!=null)tailer=new LogTailer(tailer.Folder);
        UpdateChannelSummary();RefreshIntel();UpdateAlert();Save();
    }
    private void ShowChannelSettings()
    {
        var names=new HashSet<string>(settings.IntelChannels ?? ["Intel.WOMP"],StringComparer.OrdinalIgnoreCase){"Intel.WOMP","Corp"};
        try{foreach(var file in Directory.EnumerateFiles(tailer?.Folder??LogFolderLocator.Resolve(),"*.txt"))names.Add(IntelChannel.Name(Path.GetFileNameWithoutExtension(file)));}catch(IOException){}catch(UnauthorizedAccessException){}
        var dialog=new Window{Title="Intel channels",Width=440,Height=500,Background=Brush.Parse("#0E1723")};
        var list=new StackPanel{Spacing=6};var checks=new List<CheckBox>();
        void Add(string name,bool selected){if(checks.Any(c=>string.Equals(c.Content?.ToString(),name,StringComparison.OrdinalIgnoreCase)))return;var check=new CheckBox{Content=name,IsChecked=selected};checks.Add(check);list.Children.Add(check);}
        foreach(var name in names.Order())Add(name,IntelChannel.Selected(name,settings.IntelChannels??[]));
        var custom=new TextBox{Watermark="Exact channel name"};var add=new Button{Content="Add channel"};add.Click+=(_,_)=>{if(!string.IsNullOrWhiteSpace(custom.Text)){Add(custom.Text.Trim(),true);custom.Text="";}};
        var save=new Button{Content="Apply selected channels"};save.Click+=(_,_)=>{SetIntelChannels(checks.Where(c=>c.IsChecked==true).Select(c=>c.Content!.ToString()!));dialog.Close();};
        var body=new Grid{Margin=new Thickness(18),RowDefinitions=new RowDefinitions("Auto,*,Auto,Auto,Auto")};
        body.Children.Add(new TextBlock{Text="Only selected channels feed intel, map reports and alerts. Corp is excluded unless you select it.",TextWrapping=TextWrapping.Wrap});
        var scroll=new ScrollViewer{Content=list,Margin=new Thickness(0,10)};Grid.SetRow(scroll,1);body.Children.Add(scroll);
        Grid.SetRow(custom,2);body.Children.Add(custom);Grid.SetRow(add,3);body.Children.Add(add);Grid.SetRow(save,4);body.Children.Add(save);dialog.Content=body;dialog.Show(this);
    }
}
