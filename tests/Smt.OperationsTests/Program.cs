using Smt.Core;
using System.Net;
using System.Text.Json;
var u=new Universe(args.FirstOrDefault()??"data/universe.json");int passed=0;
void Check(bool result,string label){if(!result)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);passed++;}
void Reject(Action action,string label){try{action();}catch(ArgumentException){Check(true,label);return;}catch(FormatException){Check(true,label);return;}throw new Exception("FAIL: "+label);}
JsonElement Json(string s)=>JsonDocument.Parse(s).RootElement.Clone();
var none=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
Check(u.Data.Systems.All(s=>s.ActualX!=null && s.ActualY!=null && s.ActualZ!=null),"All systems have real 3D coordinates");
var a=u.Systems["1DQ1-A"];var b=u.Systems["Jita"];
Check(Navigation.Distance(a,a)==0 && Math.Abs(Navigation.Distance(a,b)-Navigation.Distance(b,a))<1e-10,"3D distance symmetry and zero");
Check(Navigation.Range(0,5)==7 && Navigation.Range(1,5)==6 && Navigation.Range(2,5)==10 && Navigation.Range(4,5)==8,"Hull ranges with JDC V");
Check(!Navigation.JumpDestination(b) && !Navigation.JumpDestination(u.Systems["Niarja"]),"Highsec and Pochven excluded from cyno destinations");
var gates=Navigation.Plan(u,"1DQ1-A","5BTK-M",[],none,false,[]);
Check(gates.Systems.Count>0 && gates.Legs.All(l=>l.Kind=="Gate"),"Gate routing preserved");
var bridges=Navigation.ParseBridges("# network\n1DQ1-A,5BTK-M",u);
var bridged=Navigation.Plan(u,"1DQ1-A","5BTK-M",[],none,false,bridges);
Check(bridged.Legs.Count==1 && bridged.Legs[0].Kind=="Ansiblex","Bridge is used as one hop");
Check(Navigation.Plan(u,"5BTK-M","1DQ1-A",[],none,false,bridges).Legs.Count==1,"Bridge traversal is bidirectional");
Reject(()=>Navigation.ParseBridges("Jita,Amarr",u),"Highsec bridge import rejected");
Reject(()=>Navigation.ParseBridges("unknown,1DQ1-A",u),"Unknown bridge endpoint rejected");
Reject(()=>Navigation.Plan(u,"1DQ1-A","Jita",[],new HashSet<string>{"Jita"},false,[]),"Avoided destination rejected");
Reject(()=>Navigation.Plan(u,"1DQ1-A","Jita",[],none,false,[],7),"Highsec capital route rejected");
var capital=Navigation.Plan(u,"1DQ1-A","5BTK-M",[],none,false,[],7);
Check(capital.Legs.Count>0 && capital.Legs.All(l=>l.Kind=="Cyno" && l.LightYears<=7 && Navigation.JumpDestination(l.To)),"Capital path respects range and destination restrictions");
var waypoint=Navigation.Plan(u,"1DQ1-A","1DQ1-A",["5BTK-M"],none,false,bridges);
Check(waypoint.Systems.Select(s=>s.Name).SequenceEqual(new[]{"1DQ1-A","5BTK-M","1DQ1-A"}),"Ordered waypoint round trip");
var activity=Situational.ParseActivity(Json("[{\"system_id\":1,\"ship_kills\":3,\"pod_kills\":2,\"npc_kills\":5}]"),Json("[{\"system_id\":1,\"ship_jumps\":40},{\"system_id\":2,\"ship_jumps\":7}]"));
Check(activity[1].Ships==3 && activity[1].Jumps==40 && activity[2].Ships==0,"Activity feed joins kills and jumps");
var wh=Situational.ParseWormholes(Json("[{\"signature_type\":\"wormhole\",\"in_system_name\":\"Jita\",\"expires_at\":\"2099-01-01T00:00:00Z\",\"completed\":true},{\"signature_type\":\"wormhole\",\"in_system_name\":\"Amarr\",\"expires_at\":\"2000-01-01T00:00:00Z\"}]"),"Thera",DateTimeOffset.UtcNow);
Check(wh.Count==1 && wh[0].System=="Jita","Expired wormholes removed");
var storms=Situational.ParseStorms("<table><tr><td>Delve</td><td>1DQ1-A</td><td>Plasma &amp; A</td><td>Plasma</td></tr></table>",u);
Check(storms.Count==1 && storms[0].Name=="Plasma & A","Storm HTML parsing and entity decoding");
var kill=Situational.ParseKill(Json("{\"killmail_id\":123,\"esi\":{\"solar_system_id\":30000142,\"killmail_time\":\"2026-10-07T12:00:00Z\",\"victim\":{\"ship_type_id\":670}},\"zkb\":{\"totalValue\":500}}"));
Check(kill?.SystemId==30000142 && kill.Value==500 && kill.ShipType==670,"R2Z2 envelope parsed");
var rateHandler=new Stub(req=>new(HttpStatusCode.TooManyRequests){Content=new StringContent("{}")});using var rateApi=new LiveApi(rateHandler);
for(int i=0;i<2;i++)try{await rateApi.Json("https://esi.evetech.net/test",default);}catch(HttpRequestException){}
Check(rateHandler.Calls==1,"Rate limit blocks repeated requests during backoff");
Console.WriteLine($"{passed} operations checks passed.");
if(args.Contains("--live"))
{
 using var publicApi=new LiveApi();var live=new Situational(publicApi,u);await live.Refresh(default);
 foreach(var status in live.Status.Values)Console.WriteLine("LIVE: "+status);
 Console.WriteLine($"LIVE COUNTS: activity={live.Activity.Count}; wormholes={live.Wormholes.Count}; storms={live.Storms.Count}; kills={live.Kills.Count}");
}
sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler{public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct){Calls++;return Task.FromResult(respond(req));}}
