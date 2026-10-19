namespace Smt.Core;
public sealed class AlertNotifications
{
    private bool red;
    private string lastNearby="";
    private DateTimeOffset lastYellow=DateTimeOffset.MinValue;
    public void ResetLocal()=>red=false;
    public string[] Observe(AlertPanels panels,DateTimeOffset now)
    {
        var result=new List<string>();
        if(panels.Local.Level=="Red"&&!red)result.Add("Red");
        red=panels.Local.Level=="Red";
        if(panels.Nearby?.Level!="Yellow")lastNearby="";
        else if(panels.Nearby.Detail!=lastNearby && now-lastYellow>=TimeSpan.FromSeconds(15))
        {result.Add("Yellow");lastNearby=panels.Nearby.Detail;lastYellow=now;}
        return result.ToArray();
    }
}
