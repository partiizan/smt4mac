using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Globalization;
using Smt.Core;

namespace Smt.Desktop;

public sealed class StarMap : Control
{
    private Universe? universe;
    private Region? region;
    private double scale = 1;
    private Point offset;
    private Point pressed, last;
    private bool dragging, moved;
    private readonly Dictionary<string, Point> screen = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<RouteLeg> RouteLegs { get; set; } = [];
    public IReadOnlyList<Bridge> Bridges { get; set; } = [];
    public IReadOnlyList<Kill> Kills { get; set; } = [];
    public IReadOnlyList<Wormhole> Wormholes { get; set; } = [];
    public Dictionary<string,int> StormAreas { get; set; } = [];
    public IReadOnlyDictionary<long,Activity> Activity { get; set; } = new Dictionary<long,Activity>();
    public int ActivityMode { get; set; }
    public double? AdmThreshold { get; set; }
    public bool ShowAdm { get; set; } = true;
    public bool AdmStale { get; set; }
    public IReadOnlyDictionary<long,SystemSovereignty> Sovereignty { get; set; } = new Dictionary<long,SystemSovereignty>();
    public string? Selected { get; set; }
    public IReadOnlyList<StarSystem> Route { get; set; } = [];
    public IReadOnlyDictionary<string, IntelReport> Reports { get; set; } = new Dictionary<string, IntelReport>();
    public event Action<string>? SystemSelected;
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    private static IBrush Brush(string hex) => SolidColorBrush.Parse(hex);
    public StarMap()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Fit();
    }
    public void SetRegion(Universe data, Region map) { universe = data; region = map; Fit(); }
    public void Fit()
    {
        if (region == null || Bounds.Width < 1 || Bounds.Height < 1) return;
        var minX = region.Nodes.Min(n => n.X); var maxX = region.Nodes.Max(n => n.X);
        var minY = region.Nodes.Min(n => n.Y); var maxY = region.Nodes.Max(n => n.Y);
        scale = Math.Max(.05, Math.Min((Bounds.Width - 100) / Math.Max(1, maxX-minX), (Bounds.Height - 90) / Math.Max(1, maxY-minY)));
        offset = new Point(Bounds.Width/2 - (minX+maxX)/2*scale, Bounds.Height/2 - (minY+maxY)/2*scale);
        InvalidateVisual();
    }
    public void Zoom(double factor) => ZoomAt(factor, new Point(Bounds.Width/2, Bounds.Height/2));
    private void ZoomAt(double factor, Point anchor)
    {
        var next = Math.Clamp(scale*factor, .08, 12); factor = next/scale;
        offset = new Point(anchor.X-(anchor.X-offset.X)*factor, anchor.Y-(anchor.Y-offset.Y)*factor);
        scale = next; InvalidateVisual();
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { ZoomAt(Math.Pow(1.16, e.Delta.Y), e.GetPosition(this)); e.Handled = true; }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressed = last = e.GetPosition(this); dragging = true; moved = false; e.Pointer.Capture(this);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!dragging) return;
        var p=e.GetPosition(this); if (Distance(p,pressed) > 4) moved=true;
        if (moved) { offset += p-last; InvalidateVisual(); } last=p;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!dragging) return;
        dragging=false; e.Pointer.Capture(null);
        if (moved) return;
        var p=e.GetPosition(this); var nearest=screen.OrderBy(k=>Distance(k.Value,p)).FirstOrDefault();
        if (nearest.Key != null && Distance(nearest.Value,p) < 18) { Selected=nearest.Key; SystemSelected?.Invoke(nearest.Key); InvalidateVisual(); }
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => dragging=false;
    private static void Text(DrawingContext c, string text, Point position, IBrush color, double size=11)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), size, color);
        c.DrawText(formatted, new Point(position.X-formatted.Width/2, position.Y));
    }
    public override void Render(DrawingContext c)
    {
        base.Render(c);
        c.FillRectangle(Brush("#0A111B"), new Rect(Bounds.Size));
        var grid = Brush("#192333");
        for (double x=24; x<Bounds.Width; x+=32) for(double y=16; y<Bounds.Height; y+=32) c.DrawEllipse(grid,null,new Point(x,y),.7,.7);
        if (region == null || universe == null) return;
        screen.Clear();
        foreach (var n in region.Nodes) screen[n.Name]=new Point(n.X*scale+offset.X,n.Y*scale+offset.Y);
        var routeEdges = new HashSet<string>();
        for(int i=1;i<Route.Count;i++) { routeEdges.Add(Route[i-1].Name+"|"+Route[i].Name); routeEdges.Add(Route[i].Name+"|"+Route[i-1].Name); }
        foreach(var n in region.Nodes)
        {
            if(!universe.Systems.TryGetValue(n.Name,out var sys)) continue;
            foreach(var jump in sys.Jumps)
            {
                if(string.CompareOrdinal(n.Name,jump)>=0 || !screen.TryGetValue(jump,out var end)) continue;
                bool routed=routeEdges.Contains(n.Name+"|"+jump);
                c.DrawLine(new Pen(Brush(routed?"#55DFC5":"#344256"),routed?2.8:1),screen[n.Name],end);
            }
        }
        foreach(var b in Bridges)
            if(screen.TryGetValue(b.From,out var bp) && screen.TryGetValue(b.To,out var bq))c.DrawLine(new Pen(Brush("#B68AFF"),1.5),bp,bq);
        foreach(var l in RouteLegs.Where(l=>l.Kind!="Gate"))
            if(screen.TryGetValue(l.From.Name,out var lp) && screen.TryGetValue(l.To.Name,out var lq))c.DrawLine(new Pen(Brush("#55DFC5"),3),lp,lq);
        foreach(var n in region.Nodes)
        {
            var p=screen[n.Name]; if(p.X < -70 || p.Y < -30 || p.X>Bounds.Width+70 || p.Y>Bounds.Height+30) continue;
            var sys=universe.Systems[n.Name];
            if(Sovereignty.TryGetValue(sys.Id,out var filteredSov) && AdmFilter.Matches(filteredSov.Adm,AdmThreshold))
                c.DrawEllipse(null,new Pen(Brush("#FFC65C"),3),p,22,22);
            if(StormAreas.TryGetValue(n.Name,out var strength))c.DrawEllipse(Brush(strength==2?"#446E55AE":"#226E55AE"),new Pen(Brush("#987BD0"),.5),p,strength==2?25:20,strength==2?25:20);
            if(ActivityMode>0 && Activity.TryGetValue(sys.Id,out var a))
            {
                var count=ActivityMode switch {1=>a.Ships,2=>a.Pods,3=>a.Npcs,4=>a.Jumps,_=>0};
                if(count>0){var radius=7+Math.Min(23,Math.Log10(count+1)*8);c.DrawEllipse(Brush("#3377B9F3"),new Pen(Brush("#77B9F3"),1),p,radius,radius);Text(c,count.ToString(),new Point(p.X,p.Y-24),Brush("#77B9F3"),10);}
            }
            if(Wormholes.Any(w=>w.System==n.Name && w.Expires>DateTimeOffset.UtcNow))Text(c,"WH",new Point(p.X-17,p.Y-15),Brush("#D9B6FF"),10);
            if(Kills.Any(k=>k.SystemId==sys.Id && k.Time>DateTimeOffset.UtcNow.AddMinutes(-15)))Text(c,"×",new Point(p.X+18,p.Y+3),Brush("#FF776F"),17);
            var color=Brush(sys.Security>=.45?"#75D3BF":sys.Security>0?"#EBC077":"#C78C9B");
            if(Reports.TryGetValue(n.Name,out var report) && DateTimeOffset.UtcNow-report.Time < TimeSpan.FromMinutes(15) && report.Time <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                var alert=Brush(report.Clear?"#66D3A7":"#FF997E");
                c.DrawEllipse(null,new Pen(alert,2),p,13,13); c.DrawEllipse(null,new Pen(alert,.4),p,18,18);
            }
            if(n.Name==Selected) c.DrawEllipse(null,new Pen(Brush("#F0F6FC"),1.5),p,10,10);
            c.DrawEllipse(Brush("#0A111B"),new Pen(color,n.Outside?1:2),p,4,4);
            if(!n.Outside) c.DrawEllipse(color,null,p,1.8,1.8);
            Text(c,n.Name,new Point(p.X,p.Y+8),Brush(n.Outside?"#75869B":"#DCE5EF"),10);
            if(ShowAdm)
            {
                string label="ADM —";
                if(Sovereignty.TryGetValue(sys.Id,out var sov))
                    label=sov.Adm.HasValue? $"{sov.Adm.Value.ToString("0.0",CultureInfo.InvariantCulture)}×"+(AdmStale?"*":"") : sov.Kind is "Faction" or "Unclaimed" ? "N/A" : "ADM —";
                Text(c,label,new Point(p.X,p.Y+20),Brush(AdmStale?"#D6B879":"#55DFC5"),9);
            }
        }
    }
}
