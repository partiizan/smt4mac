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
        grid.ColumnDefinitions[2].Width=new GridLength(double.IsFinite(settings.IntelWidth)?Math.Clamp(settings.IntelWidth,280,600):340);
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
            height=Math.Clamp(initialHeight+start-e.GetPosition(grid).Y,280,Math.Max(280,grid.Bounds.Height-396));Apply();
        };
        divider.PointerReleased+=(_,e)=>{if(dragY==null)return;dragY=null;e.Pointer.Capture(null);settings=settings with{WormholeHeight=height};Save();};
        divider.PointerCaptureLost+=(_,_)=>dragY=null;
        Get<GridSplitter>("IntelDivider").PointerReleased+=(_,_)=>{settings=settings with{IntelWidth=Get<Border>("IntelPanel").Bounds.Width};Save();};
        Closing+=(_,_)=>{settings=settings with{IntelWidth=Get<Border>("IntelPanel").Bounds.Width,WormholeHeight=height};Save();};
        Apply();
    }
}
