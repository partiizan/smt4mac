using Smt.Core;
using System.Text;

var path = args.Length > 0 ? args[0] : "data/universe.json";
var universe = new Universe(path);
int passed=0;
void Check(bool condition,string name) { if(!condition) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); passed++; }
Check(universe.Systems.Count>5000,"Bundled real universe loads");
Check(universe.Data.Regions.All(r=>r.Nodes.All(n=>universe.Systems.ContainsKey(n.Name))),"Every map node resolves to a real system");
Check(universe.Route("Jita","Perimeter").Count==2,"Known adjacent systems use one jump");
var route=universe.Route("1DQ1-A","Jita");
Check(route.Count>2 && route.Zip(route.Skip(1)).All(p=>p.First.Jumps.Contains(p.Second.Name)),"Cross-region route follows real stargates");
Check(universe.Route("Jita","Jita").Count==1,"Same-system route has zero jumps");
Check(universe.Route("invalid","Jita").Count==0,"Unknown endpoint rejected");
Check(universe.Route("1DQ1-A","Jita",true).Count==0,"High-sec route excludes null-sec endpoint");
Check(universe.Route("Jita","Perimeter",true).All(s=>s.Security>=.45),"High-sec route restriction enforced");
Check(universe.Search("1dq").First().Name=="1DQ1-A","Case-insensitive system search");
var parser=new IntelParser(universe); var now=DateTimeOffset.UtcNow;
string Stamp(string s)=>$"[ {now:yyyy.MM.dd HH:mm:ss} ] Scout > {s}";
Check(parser.Parse(Stamp("1dq1-a three neut, T5ZI-S clear"),"intel",now)?.Systems.Length==2,"Intel matches canonical system names case-insensitively");
Check(parser.Parse(Stamp("Jita clear"),"intel",now)?.Clear==true,"Clear report recognized");
Check(parser.Parse(Stamp("Jita not clear"),"intel",now)?.Clear==false,"Negated clear is not clear");
Check(parser.Parse(Stamp("JitaX"),"intel",now)==null,"Partial system names do not match");
Check(parser.Parse("Channel Name: Jita","header",now)==null,"Log headers do not create reports");
Check(parser.Parse(Stamp("New Caldari clear"),"intel",now)?.Systems.Contains("New Caldari")==true,"Multiword system names match");
Check(parser.Parse("Jita a hostile reported","manual",now,true)!=null,"Manual intel accepted explicitly");
Check(parser.Parse("[ 2026.99.99 25:99:99 ] Scout > Jita","bad",now)==null,"Malformed log time rejected");
Check(parser.Parse("\uFEFF"+Stamp("9CG6-H test"),"Fleet",now)?.Systems.Single()=="9CG6-H","Per-message BOM no longer blocks EVE intel parsing");
Check(parser.Parse(" \t\uFEFF\uFEFF "+Stamp("9CG6-H test"),"Fleet",now)!=null,"Repeated BOMs mixed with leading whitespace accepted");
Check(parser.Parse("\uFEFFChannel Name: Jita","Fleet",now)==null,"BOM-prefixed headers remain ignored");
Check(AdmFilter.Matches(3.99,4) && !AdmFilter.Matches(4,4),"ADM below 4 is strict at the boundary");
Check(AdmFilter.Matches(4.99,5) && !AdmFilter.Matches(5,5),"ADM below 5 is strict at the boundary");
Check(!AdmFilter.Matches(null,4) && !AdmFilter.Matches(double.NaN,5) && !AdmFilter.Matches(0,5),"Missing and invalid ADM values never highlighted");
Check(!AdmFilter.Matches(3,null),"ADM filter off produces no highlight");
var bomDir=Path.Combine(Path.GetTempPath(),"smt-bom-"+Guid.NewGuid());Directory.CreateDirectory(bomDir);
try
{
 var file=Path.Combine(bomDir,"Fleet_test.txt");
 // Real macOS EVE layout: UTF16 file BOM, LF headers, another BOM before CRLF messages.
 File.WriteAllText(file,"\r\n\r\n\n\n    Channel Name: Fleet\n\n\uFEFF"+Stamp("9CG6-H test")+"\r\n",Encoding.Unicode);
 var watcher=new LogTailer(bomDir);var first=watcher.Poll();
 Check(first.Select(l=>parser.Parse(l.Line,l.Source,now)).Count(r=>r!=null)==1,"UTF16 EVE header and embedded BOM produce one report through tailer");
 Check(watcher.FilesFound==1 && watcher.EligibleFiles==1 && watcher.FilesRead==1 && watcher.BytesRead>0,"File reader exposes successful scan metrics");
 Check(watcher.Poll().Count==0 && watcher.BytesRead==0,"Idle scan does not replay BOM-prefixed report");
 var added=Encoding.Unicode.GetBytes("\uFEFF"+Stamp("9CG6-H clear")+"\r\n");
 using(var append=new FileStream(file,FileMode.Append)){append.Write(added,0,1);}
 Check(watcher.Poll().Count==0,"Partial UTF16 BOM waits for complete message");
 using(var append=new FileStream(file,FileMode.Append)){append.Write(added,1,added.Length-1);}
 Check(watcher.Poll().Select(l=>parser.Parse(l.Line,l.Source,now)).Single()?.Clear==true,"Split BOM and appended clear message decode correctly");
}
finally{Directory.Delete(bomDir,true);}
var dir=Path.Combine(Path.GetTempPath(),"smt-test-"+Guid.NewGuid()); Directory.CreateDirectory(dir);
try
{
    foreach(var encoding in new[]{Encoding.UTF8,Encoding.Unicode,Encoding.BigEndianUnicode})
    {
        var file=Path.Combine(dir,encoding.WebName+".txt");
        File.WriteAllText(file,Stamp("Jita clear")+"\r\n",encoding);
    }
    var tailer=new LogTailer(dir);
    Check(tailer.Poll().Count==3,"UTF-8, UTF-16LE and UTF-16BE read");
    Check(tailer.Poll().Count==0,"Unchanged files are not replayed");
    var utf=Path.Combine(dir,"utf-8.txt");
    File.AppendAllText(utf,Stamp("Perimeter"),new UTF8Encoding(false));
    Check(tailer.Poll().Count==0,"Partial log line waits for newline");
    File.AppendAllText(utf," clear\n",new UTF8Encoding(false));
    Check(tailer.Poll().Single().Line.EndsWith("Perimeter clear"),"Split writes produce one complete line");
    File.WriteAllText(utf,"x\n",new UTF8Encoding(false));
    Check(tailer.Poll().Count==0,"Truncated file waits for encoding probe");
    File.AppendAllText(utf,"y\n",new UTF8Encoding(false));
    Check(tailer.Poll().Count==2,"Truncation resets cursor");
    var split=Path.Combine(dir,"split.txt");
    var bytes=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Stamp("Jita clear")+"\n")).ToArray();
    File.WriteAllBytes(split,bytes[..^3]);
    Check(tailer.Poll().Count==0,"Split UTF-16 character is buffered");
    using(var stream=new FileStream(split,FileMode.Append)) stream.Write(bytes[^3..]);
    Check(tailer.Poll().Single().Line.EndsWith("Jita clear"),"UTF-16 decoder preserves split characters");
    File.WriteAllText(Path.Combine(dir,"new-channel.txt"),Stamp("Jita clear")+"\n");
    Check(tailer.Poll().Count==1,"Newly created channel logs are discovered");
}
finally { Directory.Delete(dir,true); }
// Automatic discovery across users and late EVE startup.
var autoHome=Path.Combine(Path.GetTempPath(),"smt-auto-"+Guid.NewGuid());
try
{
 var expected=Path.Combine(autoHome,"Documents","EVE","logs","chatlogs");
 Check(LogFolderLocator.Resolve(home:autoHome,documents:"")==expected,"Default chatlogs path resolves relative to current user's home");
 var monitor=new LogTailer(expected);bool waiting=false;
 try { monitor.Poll(); } catch(DirectoryNotFoundException) { waiting=true; }
 Check(waiting,"Missing folder waits without creating or modifying EVE directories");
 Directory.CreateDirectory(expected);
 File.WriteAllText(Path.Combine(expected,"Alliance_20261007.txt"),Stamp("1DQ1-A hostile")+"\n",Encoding.Unicode);
 Check(monitor.Poll().Single().Line.Contains("1DQ1-A"),"Existing monitor discovers folder created after launch");
 Check(monitor.Poll().Count==0,"Automatic initial ingestion is not repeated");
 File.AppendAllText(Path.Combine(expected,"Alliance_20261007.txt"),Stamp("T5ZI-S clear")+"\n",Encoding.Unicode);
 Check(monitor.Poll().Single().Line.Contains("T5ZI-S"),"Automatic monitor ingests appended UTF16 report");
 Check(LogFolderLocator.Resolve("~/custom",autoHome,"")==Path.Combine(autoHome,"custom"),"Custom home-relative override expands universally");
 Check(LogFolderLocator.Resolve("/custom/logs",autoHome,"")=="/custom/logs","Saved custom folder takes precedence");
 var caps=Path.Combine(autoHome,"Documents","EVE","logs","Chatlogs");Directory.Move(expected,caps);
 Check(File.Exists(Path.Combine(LogFolderLocator.Resolve(home:autoHome,documents:""),"Alliance_20261007.txt")),"Chatlogs capitalization resolves to the actual logs on this filesystem");
 Check(LogFolderLocator.Resolve(home:autoHome+"-other",documents:"")!=expected,"Another username gets its own default folder");
 File.WriteAllText(Path.Combine(caps,"blocked.txt"),Stamp("Jita clear")+"\n");
 using(var locked=new FileStream(Path.Combine(caps,"blocked.txt"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
 {
  var resilient=new LogTailer(caps);var rows=resilient.Poll();
  Check(rows.Any(r=>r.Line.Contains("T5ZI-S"))&&resilient.ReadErrors.Count==1,"Locked file does not block other channel logs");
 }
}
finally { if(Directory.Exists(autoHome))Directory.Delete(autoHome,true); }
// ADM fixtures deliberately distinguish zero development indexes from missing ADM.
var sovJson="""
{"solar_systems":[
 {"solar_system_id":1,"claim":{"alliance":{"is_capital_system":true,"development":{"activity_defense_multiplier":4.1,"military_level":4,"industrial_level":0,"strategic_level":5}}}},
 {"solar_system_id":2,"claim":{"faction":{"faction_id":500001}}},
 {"solar_system_id":3},
 {"solar_system_id":4,"claim":{"alliance":{"alliance_id":1}}}
]}
""";
var sov=SovereigntyClient.Parse(sovJson);
Check(sov[1].Adm==4.1 && sov[1].Industrial==0 && sov[1].Capital,"ADM, zero industry index and capital parsed from current ESI schema");
Check(sov[2].Kind=="Faction" && sov[2].Adm==null,"NPC sovereignty has no fabricated ADM");
Check(sov[3].Kind=="Unclaimed" && sov[3].Adm==null,"Unclaimed system ADM remains absent");
Check(sov[4].Kind=="Alliance" && sov[4].Adm==null,"Missing alliance ADM remains unavailable");
Check(SovereigntyClient.Parse(sovJson.Replace("4.1","99"))[1].Adm==null,"Out-of-range ADM not displayed");
bool bad=false;try{SovereigntyClient.Parse("{}");}catch(System.Text.Json.JsonException){bad=true;}
Check(bad,"Malformed response rejected");
var admDir=Path.Combine(Path.GetTempPath(),"smt-adm-test-"+Guid.NewGuid());
var tick=DateTimeOffset.UtcNow;
var fake=new FakeEsi(sovJson);
using(var esi=new SovereigntyClient(Path.Combine(admDir,"sov.json"),fake,()=>tick))
{
 await esi.RefreshAsync();
 Check(esi.Systems[1].Adm==4.1 && esi.CheckedAt==tick && !esi.IsStale,"Successful ESI response updates values and freshness");
 Check(fake.LastCompatibility=="2026-05-19" && !fake.HadAuthorization,"Public ESI compatibility header, no credentials");
 await esi.RefreshAsync(); Check(fake.Calls==1,"Five-minute cache prevents request hammering");
 tick=tick.AddMinutes(6);fake.Status=System.Net.HttpStatusCode.NotModified;
 await esi.RefreshAsync();Check(fake.LastEtag=="\"fixture\"" && esi.CheckedAt==tick && esi.Systems[1].Adm==4.1,"304 revalidates cached ADM with ETag");
 tick=tick.AddMinutes(6);fake.Status=System.Net.HttpStatusCode.ServiceUnavailable;
 await esi.RefreshAsync();Check(esi.Error!=null && esi.IsStale && esi.Systems[1].Adm==4.1,"ESI outage keeps last values explicitly stale");
 tick=tick.AddMinutes(6);fake.Status=System.Net.HttpStatusCode.TooManyRequests;
 await esi.RefreshAsync();Check(esi.NextCheck>=tick.AddMinutes(20),"ESI Retry-After respected");
}
using(var cached=new SovereigntyClient(Path.Combine(admDir,"sov.json"),clock:()=>tick))
 Check(cached.Systems[1].Adm==4.1 && cached.FromDisk && cached.IsStale,"Restart loads cache marked unverified");
Directory.Delete(admDir,true);
Console.WriteLine($"{passed} checks passed.");

sealed class FakeEsi(string json):HttpMessageHandler
{
 public int Calls; public string? LastCompatibility,LastEtag;public bool HadAuthorization;
 public System.Net.HttpStatusCode Status=System.Net.HttpStatusCode.OK;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {
  Calls++;LastCompatibility=request.Headers.GetValues("X-Compatibility-Date").Single();
  LastEtag=request.Headers.TryGetValues("If-None-Match",out var e)?e.Single():null;HadAuthorization=request.Headers.Authorization!=null;
  var response=new HttpResponseMessage(Status){Content=new StringContent(json)};
  response.Headers.ETag=new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture\"");
  if(Status==System.Net.HttpStatusCode.TooManyRequests) response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(20));
  return Task.FromResult(response);
 }
}
