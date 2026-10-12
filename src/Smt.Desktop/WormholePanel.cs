using Avalonia.Controls;
using Avalonia.Interactivity;
using Smt.Core;
namespace Smt.Desktop;

public sealed record WormholeRow(Wormhole Connection,string Region,bool CanMap,DateTimeOffset Now)
{
    public string Hub=>Connection.Hub;
    public string System=>Connection.System;
    public string Location=>Region+" · "+(Connection.SecurityClass.ToLowerInvariant() switch {"hs"=>"High sec","ls"=>"Low sec","ns"=>"Null sec","wh"=>"W-space",_=>"Security unknown"});
    public string Signatures=>$"{Hub}: {Display(Connection.OutSignature)}\nLocal: {Display(Connection.InSignature)}";
    public string Size=>Display(Connection.Size);
    public string Expiry=>Connection.Expires<=Now?"Expired":Connection.Expires-Now<TimeSpan.FromHours(1)?$"{Math.Max(1,(int)(Connection.Expires-Now).TotalMinutes)}m left":$"{(int)(Connection.Expires-Now).TotalHours}h {(Connection.Expires-Now).Minutes}m";
    public string ExpiryColor=>Connection.Expires-Now<TimeSpan.FromHours(2)?"#FFC65C":"#55DFC5";
    public string Details=>$"Expires {Connection.Expires:MMM d HH:mm} UTC\n"+(Connection.Updated is {} t?$"Reported update {t:MMM d HH:mm} UTC":"Update time unavailable");
    public string CopyText=>$"{Hub} ↔ {System} ({Region}) | {Signatures.Replace("\n"," · ")} | Max ship size: {Size} | Expires {Connection.Expires:yyyy-MM-dd HH:mm} UTC";
    private static string Display(string s)=>string.IsNullOrWhiteSpace(s)?"—":s;
}
public partial class MainWindow
{
    private void RefreshWormholeRows()
    {
        if(situation==null)return;
        var hub=Get<ComboBox>("WormholeHub").SelectedItem as string;
        var query=Get<TextBox>("WormholeSearch").Text?.Trim()??"";
        var now=DateTimeOffset.UtcNow;
        var rows=situation.Wormholes.Where(w=>w.Expires>now && (hub is null or "All hubs" || w.Hub==hub))
            .Select(w=>new WormholeRow(w,string.IsNullOrWhiteSpace(w.Region)?universe.Systems.GetValueOrDefault(w.System)?.Region??"Unknown region":w.Region,universe.Systems.ContainsKey(w.System),now))
            .Where(w=>query.Length==0 || (w.System+" "+w.Region+" "+w.Signatures).Contains(query,StringComparison.OrdinalIgnoreCase))
            .OrderBy(w=>w.Connection.Expires).ThenBy(w=>w.System).ToArray();
        Get<ListBox>("WormholeList").ItemsSource=rows;
        Get<TextBlock>("WormholeCount").Text=$"{rows.Length} connections · earliest expiry first";
        Get<TextBlock>("WormholeEmpty").IsVisible=rows.Length==0;
        Get<TextBlock>("WormholeEmpty").Text=query.Length>0 || hub is "Thera" or "Turnur"?"No connections match these filters.":"No current connections available. See feed status below.";
        Get<TextBlock>("WormholeStatus").Text="Eve-Scout · "+situation.Status.GetValueOrDefault("Wormholes","Connecting…");
    }
    private void ShowWormhole(object? sender,RoutedEventArgs e)
    {
        if(sender is Control {DataContext:WormholeRow row})
        {
            if(row.Connection.Expires<=DateTimeOffset.UtcNow){SetStatus("This connection has expired; wait for refreshed data.");return;}
            SelectSystem(row.System);
        }
        e.Handled=true;
    }
    private async void CopyWormhole(object? sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(sender is not Control {DataContext:WormholeRow row})return;
        try{if(Clipboard is {} clipboard){await clipboard.SetTextAsync(row.CopyText);SetStatus("Copied "+row.Hub+" / "+row.System+" connection details.");}}
        catch(Exception error){SetStatus("Could not copy: "+error.Message);}
    }
}
