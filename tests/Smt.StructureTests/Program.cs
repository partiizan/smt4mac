using Smt.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
Check(StructureSession.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")=="E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM","PKCE RFC vector");
Check(StructureSession.CallbackCode(new(StructureSession.Callback+"?state=abc&code=123"),"abc")=="123","Valid callback");
foreach(var url in new[]{StructureSession.Callback+"?state=wrong&code=123",StructureSession.Callback+"?state=abc&error=access_denied","http://example.com/?state=abc&code=123"}){
    try{StructureSession.CallbackCode(new(url),"abc");throw new Exception("Unsafe callback accepted");}catch(InvalidDataException){Check(true,"Invalid callback rejected");}
}
using var rsa=RSA.Create(2048);
foreach(var invalid in new[]{"","signature","audience","expired","scope"}){
    var handler=new FixtureHandler(rsa,invalid);
    using var session=new StructureSession(handler);
    Task? callback=null;
    try{
        await session.Connect("test-client",url=>{
            var query=new Uri(url).Query.TrimStart('?').Split('&').Select(s=>s.Split('=',2)).ToDictionary(p=>p[0],p=>Uri.UnescapeDataString(p[1]));
            Check(query["scope"]==StructureSession.Scope&&query["code_challenge_method"]=="S256","Least privilege PKCE browser request");
            handler.ExpectedChallenge=query["code_challenge"];
            callback=Task.Run(async()=>{using var browser=new HttpClient();await browser.GetStringAsync(StructureSession.Callback+"?state="+query["state"]+"&code=test-code");});
        },CancellationToken.None);
        if(invalid!="")throw new Exception("Invalid JWT accepted: "+invalid);
        Check(session.Connected&&session.CharacterName=="Test Pilot","Verified character session");
        var found=await session.Lookup(123,CancellationToken.None);
        Check(found.Name=="Test Fortizar"&&found.SystemId==30004759&&found.TypeId==35833,"Authenticated structure resolution");
        handler.Status=HttpStatusCode.Forbidden;
        try{await session.Lookup(124,CancellationToken.None);throw new Exception("403 accepted");}catch(InvalidDataException e){Check(e.Message.Contains("403"),"403 explained without tokens");}
        handler.Status=HttpStatusCode.TooManyRequests;
        try{await session.Lookup(125,CancellationToken.None);throw new Exception("429 accepted");}catch(InvalidDataException){Check(true,"429 backoff");}
        session.Disconnect();Check(!session.Connected,"Disconnect clears authorization");
    }catch(InvalidDataException){if(invalid=="")throw;Check(!session.Connected,"Reject "+invalid+" JWT");}
    if(callback!=null)await callback;
}
Console.WriteLine("Structure SSO protocol tests passed (synthetic credentials; no real login).");

sealed class FixtureHandler(RSA rsa,string invalid):HttpMessageHandler
{
    public string ExpectedChallenge="";
    public HttpStatusCode Status=HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
    {
        object data;
        var path=req.RequestUri!.AbsolutePath;
        if(path.EndsWith("/token")){
            var form=(await req.Content!.ReadAsStringAsync(ct)).Split('&').Select(s=>s.Split('=',2)).ToDictionary(p=>p[0],p=>Uri.UnescapeDataString(p[1]));
            if(form["code"]!="test-code"||StructureSession.Challenge(form["code_verifier"])!=ExpectedChallenge)throw new Exception("PKCE exchange mismatch");
            var header=StructureSession.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new{alg="RS256",kid="test"}));
            var payload=StructureSession.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new{iss="https://login.eveonline.com/",aud=new[]{"EVE Online",invalid=="audience"?"wrong":"test-client"},exp=DateTimeOffset.UtcNow.AddMinutes(invalid=="expired"?-1:20).ToUnixTimeSeconds(),sub="CHARACTER:EVE:123",name="Test Pilot",scp=new[]{invalid=="scope"?"wrong":StructureSession.Scope}}));
            var bytes=rsa.SignData(Encoding.ASCII.GetBytes(header+"."+payload),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);if(invalid=="signature")bytes[0]^=1;
            data=new{access_token=header+"."+payload+"."+StructureSession.Base64Url(bytes),refresh_token="synthetic-refresh"};
        }else if(path.Contains(".well-known"))data=new{jwks_uri="https://login.eveonline.com/keys"};
        else if(path=="/keys"){
            var key=rsa.ExportParameters(false);data=new{keys=new[]{new{kid="test",kty="RSA",n=StructureSession.Base64Url(key.Modulus!),e=StructureSession.Base64Url(key.Exponent!)}}};
        }else{
            if(req.Headers.Authorization?.Scheme!="Bearer")throw new Exception("Missing bearer authorization");
            if(Status!=HttpStatusCode.OK)return new(Status){Content=new StringContent("{}")};
            data=new{name="Test Fortizar",solar_system_id=30004759L,type_id=35833L};
        }
        return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data))};
    }
}
