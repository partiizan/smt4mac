using Avalonia;
using Avalonia.Controls;
namespace Smt.Desktop;
public partial class MainWindow
{
    private void InitializeLayout()
    {
        var grid=Get<Grid>("Workspace");
        var divider=Get<Border>("WormholeDivider");
        var toggle=Get<Button>("ToggleWormholes");
        double height=double.IsFinite(settings.WormholeHeight)?Math.Clamp(settings.WormholeHeight,280,600):330;
        double intelWidth=double.IsFinite(settings.IntelWidth)?Math.Clamp(settings.IntelWidth,240,480):300;
        grid.ColumnDefinitions[2].Width=new GridLength(double.IsFinite(settings.MonitorWidth)?Math.Clamp(settings.MonitorWidth,260,480):300);
        void ApplyIntel()
        {
            bool collapsed=settings.IntelCollapsed;
            Get<Border>("IntelPanel").IsVisible=!collapsed;
            Get<Button>("ExpandIntel").IsVisible=collapsed;
            Get<Border>("MonitorDivider").IsVisible=!collapsed;
            grid.ColumnDefinitions[3].Width=new GridLength(collapsed?0:8);
            grid.ColumnDefinitions[4].MinWidth=collapsed?36:240;
            grid.ColumnDefinitions[4].Width=new GridLength(collapsed?36:intelWidth);
        }
        void SaveWidths()
        {
            if(!settings.IntelCollapsed && Get<Border>("IntelPanel").Bounds.Width>=240)
                intelWidth=Get<Border>("IntelPanel").Bounds.Width;
            settings=settings with{IntelWidth=intelWidth,MonitorWidth=Get<Border>("MonitorPanel").Bounds.Width};
            Save();
        }
        Get<Button>("CollapseIntel").Click+=(_,_)=>{SaveWidths();settings=settings with{IntelCollapsed=true};ApplyIntel();Save();};
        Get<Button>("ExpandIntel").Click+=(_,_)=>{settings=settings with{IntelCollapsed=false};ApplyIntel();Save();};
        ApplyIntel();
        void Apply()
        {
            bool collapsed=settings.WormholesCollapsed;
            Get<Border>("LivePanel").IsVisible=!collapsed;divider.IsVisible=!collapsed;
            grid.RowDefinitions[1].Height=new GridLength(collapsed?0:8);
            grid.RowDefinitions[3].MinHeight=collapsed?0:280;
            grid.RowDefinitions[3].Height=new GridLength(collapsed?0:height);
            toggle.Content=collapsed?"▲ Wormholes & live feeds · click to expand":"▼ Wormholes & live feeds · click to minimize";
        }
        toggle.Click+=(_,_)=>{settings=settings with{WormholesCollapsed=!settings.WormholesCollapsed,WormholeHeight=height};Apply();Save();};
        double? dragY=null;double initialHeight=0;
        divider.PointerPressed+=(_,e)=>{if(!e.GetCurrentPoint(divider).Properties.IsLeftButtonPressed)return;dragY=e.GetPosition(grid).Y;initialHeight=height;e.Pointer.Capture(divider);e.Handled=true;};
        divider.PointerMoved+=(_,e)=>
        {
            if(dragY is not {} start)return;
            height=Math.Clamp(initialHeight+start-e.GetPosition(grid).Y,280,Math.Max(280,grid.Bounds.Height-446));Apply();
        };
        divider.PointerReleased+=(_,e)=>{if(dragY==null)return;dragY=null;e.Pointer.Capture(null);settings=settings with{WormholeHeight=height};Save();};
        divider.PointerCaptureLost+=(_,_)=>dragY=null;
        Get<GridSplitter>("IntelDivider").PointerReleased+=(_,_)=>{SaveWidths();};
        var monitorDivider=Get<Border>("MonitorDivider");
        double? dragX=null;double startIntel=0,startMonitor=0;
        monitorDivider.PointerPressed+=(_,e)=>
        {
            if(!e.GetCurrentPoint(monitorDivider).Properties.IsLeftButtonPressed)return;
            dragX=e.GetPosition(grid).X;startIntel=Get<Border>("IntelPanel").Bounds.Width;startMonitor=Get<Border>("MonitorPanel").Bounds.Width;
            e.Pointer.Capture(monitorDivider);e.Handled=true;
        };
        monitorDivider.PointerMoved+=(_,e)=>
        {
            if(dragX is not {} start)return;
            var delta=Math.Clamp(e.GetPosition(grid).X-start,Math.Max(260-startMonitor,startIntel-480),Math.Min(480-startMonitor,startIntel-240));
            grid.ColumnDefinitions[2].Width=new GridLength(startMonitor+delta);intelWidth=startIntel-delta;grid.ColumnDefinitions[4].Width=new GridLength(intelWidth);
        };
        monitorDivider.PointerReleased+=(_,e)=>
        {
            if(dragX==null)return;dragX=null;e.Pointer.Capture(null);
            SaveWidths();
        };
        monitorDivider.PointerCaptureLost+=(_,_)=>dragX=null;
        Closing+=(_,_)=>{SaveWidths();settings=settings with{WormholeHeight=height};Save();};
        grid.SizeChanged+=(_,_)=>
        {
            if(grid.Bounds.Height<=0)return;
            var budget=grid.Bounds.Width-496;
            var intel=Get<Border>("IntelPanel").Bounds.Width;var monitor=Get<Border>("MonitorPanel").Bounds.Width;
            if(!settings.IntelCollapsed && budget>=500 && intel+monitor>budget+1)
            {
                intelWidth=Math.Clamp(budget*intel/(intel+monitor),240,budget-260);
                grid.ColumnDefinitions[4].Width=new GridLength(intelWidth);grid.ColumnDefinitions[2].Width=new GridLength(budget-intelWidth);
            }
            height=Math.Clamp(height,280,Math.Max(280,grid.Bounds.Height-446));Apply();
        };
        Apply();
    }
}
