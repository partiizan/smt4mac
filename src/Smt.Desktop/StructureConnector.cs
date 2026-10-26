using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Smt.Core;
namespace Smt.Desktop;
public partial class MainWindow
{
    private readonly StructureSession structureSession=new();
    private readonly Dictionary<long,KnownStructure> privateStructures=[];
    private Window? structureDialog;
    private void ShowStructureConnector()
    {
        if(structureDialog!=null){structureDialog.Activate();return;}
        var dialog=new Window{Title="SMT4Mac · Structure ESI connection",Width=660,Height=650};structureDialog=dialog;
        var panel=new StackPanel{Spacing=10,Margin=new Thickness(20)};
        TextBlock Label(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap};
        panel.Children.Add(new TextBlock{Text="Resolve accessible Upwell structures",FontSize=21,FontWeight=FontWeight.Bold});
        panel.Children.Add(Label("Optional EVE login for known structure IDs. This does not discover all structures you can dock at. No corporation role is requested."));
        panel.Children.Add(Label("One-time setup: register an Authentication & API Access application at developers.eveonline.com, enable esi-universe.read_structures.v1, and use this exact callback:"));
        panel.Children.Add(new TextBox{Text=StructureSession.Callback,IsReadOnly=true});
        var portal=new Button{Content="Open EVE application registration ↗"};portal.Click+=(_,_)=>Browse("https://developers.eveonline.com/applications");panel.Children.Add(portal);
        var client=new TextBox{Watermark="Registered application Client ID (not a secret)"};
        var clientPath=Path.Combine(SettingsStore.Folder,"esi-client-id.txt");
        try{client.Text=File.ReadAllText(clientPath);}catch(IOException){}
        panel.Children.Add(client);
        panel.Children.Add(Label("Login uses your browser and a temporary fixed localhost callback. Tokens and private structure results remain in memory only and are cleared on disconnect or app exit."));
        var status=Label(structureSession.Connected?$"Connected: {structureSession.CharacterName}":"Not connected");
        var connect=new Button{Content="Connect with EVE Online"};
        var disconnect=new Button{Content="Disconnect & clear private results"};
        var ids=new TextBox{Watermark="Structure IDs, separated by spaces, commas or newlines",AcceptsReturn=true,Height=75};
        var lookup=new Button{Content="Look up structure IDs"};
        var known=new Button{Content="Use known IDs in selected system"};
        known.Click+=(_,_)=>{if(selected!=null)ids.Text=string.Join(", ",AllStructures().Where(s=>s.SystemId==universe.Systems[selected].Id).Select(s=>s.Id));};
        var cts=CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        void Busy(bool busy){connect.IsEnabled=!busy;lookup.IsEnabled=!busy;disconnect.IsEnabled=!busy;client.IsEnabled=!busy;}
        connect.Click+=async(_,_)=>{
            Busy(true);
            try{privateStructures.Clear();ApplyFacilities();status.Text="Waiting for EVE browser login (up to 3 minutes)…";await structureSession.Connect(client.Text??"",Browse,cts.Token);Directory.CreateDirectory(SettingsStore.Folder);File.WriteAllText(clientPath,client.Text!.Trim());privateStructures.Clear();ApplyFacilities();status.Text=$"Connected: {structureSession.CharacterName}";}
            catch(Exception e) when(e is not OutOfMemoryException){structureSession.Disconnect();status.Text=e is OperationCanceledException?"Login canceled or timed out.":e is System.Net.HttpListenerException?"Login callback unavailable. Close any other SMT4Mac login and retry.":e is InvalidDataException?e.Message:"Could not connect to EVE. Check your network and application setup.";}
            finally{Busy(false);}
        };
        disconnect.Click+=(_,_)=>{structureSession.Disconnect();privateStructures.Clear();ApplyFacilities();status.Text="Disconnected; private results cleared.";};
        lookup.Click+=async(_,_)=>{
            var values=(ids.Text??"").Split(new[]{' ', ',', '\n','\r','\t',';'},StringSplitOptions.RemoveEmptyEntries);
            if(values.Length==0||values.Length>100||values.Any(v=>!long.TryParse(v,out var n)||n<=0)){status.Text="Enter 1–100 positive numeric structure IDs.";return;}
            Busy(true);var messages=new List<string>();
            try{
                foreach(var id in values.Select(long.Parse).Distinct()){
                    try{var item=await structureSession.Lookup(id,cts.Token);cts.Token.ThrowIfCancellationRequested();if(!universe.ById.ContainsKey(item.SystemId)){messages.Add($"{id}: system is outside the bundled map.");continue;}privateStructures[id]=item;messages.Add($"{item.Name} · {Facilities.TypeName(item.TypeId)} · {universe.ById[item.SystemId].Name}");}
                    catch(InvalidDataException e){privateStructures.Remove(id);messages.Add($"{id}: {e.Message}");break;}
                    ApplyFacilities();
                }
                status.Text=string.Join("\n",messages);
            }catch(Exception e) when(e is not OutOfMemoryException){status.Text="Lookup interrupted. Check connection and retry.";}
            finally{ApplyFacilities();Busy(false);}
        };
        panel.Children.Add(connect);panel.Children.Add(disconnect);panel.Children.Add(ids);panel.Children.Add(known);panel.Children.Add(lookup);panel.Children.Add(status);
        dialog.Closed+=(_,_)=>{cts.Cancel();structureDialog=null;};dialog.Content=new ScrollViewer{Content=panel};dialog.Show(this);
    }
    private KnownStructure[] AllStructures()=>knownStructures.Where(s=>!privateStructures.ContainsKey(s.Id)).Concat(privateStructures.Values).ToArray();
}
