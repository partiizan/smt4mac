using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Smt.Core;

// Optional, session-only authorization. No passwords, client secrets or tokens are written to disk.
public sealed class StructureSession : IDisposable
{
    public const string Scope="esi-universe.read_structures.v1";
    public const string Callback="http://localhost:42873/callback/";
    private readonly HttpClient http;
    private string clientId="",access="",refresh="";
    private DateTimeOffset expires,backoff;
    public string CharacterName {get;private set;}="";
    public bool Connected=>access.Length>0;
    public string LoginStage {get;private set;}="Not started";
    public string LoginFailure(Exception error)
    {
        var detail=error switch {
            InvalidDataException => error.Message,
            OperationCanceledException => "Canceled or timed out. Retry login.",
            HttpRequestException h => h.StatusCode is {} code?$"EVE returned HTTP {(int)code}. Retry shortly.":"Could not reach EVE over HTTPS. Check your connection and retry.",
            HttpListenerException => "Local callback unavailable. Close other SMT4Mac login windows and retry.",
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException => "EVE returned an unexpected response format.",
            CryptographicException => "EVE signing key or signature could not be validated.",
            _ => $"Unexpected {error.GetType().Name}. Report this step and error type."
        };
        return $"Login failed at {LoginStage}: {detail}";
    }
    private static string[] ClaimStrings(JsonElement claims,string name,bool split=false)
    {
        if(!claims.TryGetProperty(name,out var value))return [];
        if(value.ValueKind==JsonValueKind.String)return split?(value.GetString()??"").Split(' ',StringSplitOptions.RemoveEmptyEntries):[value.GetString()??""];
        if(value.ValueKind==JsonValueKind.Array&&value.EnumerateArray().All(v=>v.ValueKind==JsonValueKind.String))
            return value.EnumerateArray().Select(v=>v.GetString()!).ToArray();
        throw new InvalidDataException($"Invalid {name} claim format.");
    }
    public StructureSession(HttpMessageHandler? handler=null)
    {
        http=handler==null?new(new HttpClientHandler{AllowAutoRedirect=false}):new(handler);
        http.Timeout=TimeSpan.FromSeconds(25);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SMT4Mac/0.17 (+https://github.com/partiizan/smt4mac)");
    }
    public static string Base64Url(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static byte[] Decode(string value)=>Convert.FromBase64String(value.Replace('-','+').Replace('_','/')+new string('=',(4-value.Length%4)%4));
    public static string Challenge(string verifier)=>Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static string CallbackCode(Uri uri,string state)
    {
        if(uri.GetLeftPart(UriPartial.Path)!=Callback)throw new InvalidDataException("Unexpected login callback.");
        var values=uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=',2))
            .ToDictionary(x=>Uri.UnescapeDataString(x[0]),x=>x.Length==2?Uri.UnescapeDataString(x[1]):"");
        if(!values.TryGetValue("state",out var actual)||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual),Encoding.UTF8.GetBytes(state)))
            throw new InvalidDataException("Login state did not match. Please retry.");
        if(values.ContainsKey("error"))throw new InvalidDataException("EVE login was declined.");
        if(!values.TryGetValue("code",out var code)||string.IsNullOrWhiteSpace(code))throw new InvalidDataException("No authorization code returned.");
        return code;
    }
    public async Task Connect(string id,Action<string> openBrowser,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(id)||id.Length>200)throw new InvalidDataException("Enter your registered EVE application Client ID.");
        Disconnect();clientId=id.Trim();LoginStage="starting local callback";
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var listener=new HttpListener();listener.Prefixes.Add(Callback);listener.Start();
        using var stop=timeout.Token.Register(()=>listener.Close());
        var state=Base64Url(RandomNumberGenerator.GetBytes(32));var verifier=Base64Url(RandomNumberGenerator.GetBytes(32));
        var args=new Dictionary<string,string>{{"response_type","code"},{"client_id",clientId},{"redirect_uri",Callback},{"scope",Scope},{"state",state},{"code_challenge",Challenge(verifier)},{"code_challenge_method","S256"}};
        LoginStage="opening EVE browser login";
        openBrowser("https://login.eveonline.com/v2/oauth/authorize?"+string.Join("&",args.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value))));
        LoginStage="waiting for browser callback";
        string code;
        while(true)
        {
            var context=await listener.GetContextAsync().WaitAsync(timeout.Token);
            LoginStage="checking browser callback";
            try{code=CallbackCode(context.Request.Url!,state);}
            catch(InvalidDataException){context.Response.StatusCode=400;context.Response.Close();throw;}
            var body=Encoding.UTF8.GetBytes("Authorization received; connection is NOT complete yet. Return to SMT4Mac and wait for Connected: followed by your character name.");
            context.Response.ContentType="text/plain; charset=utf-8";context.Response.Headers["Cache-Control"]="no-store";
            await context.Response.OutputStream.WriteAsync(body,timeout.Token);context.Response.Close();break;
        }
        listener.Stop();
        await Tokens(new(){{"grant_type","authorization_code"},{"code",code},{"code_verifier",verifier},{"client_id",clientId},{"redirect_uri",Callback}},timeout.Token);
    }
    private async Task Tokens(Dictionary<string,string> values,CancellationToken ct)
    {
        LoginStage="exchanging authorization with EVE";
        using var body=new FormUrlEncodedContent(values);
        using var response=await http.PostAsync("https://login.eveonline.com/v2/oauth/token",body,ct);
        if(!response.IsSuccessStatusCode){Disconnect();throw new InvalidDataException($"EVE token endpoint returned HTTP {(int)response.StatusCode}. Check application setup and reconnect; an authorization code cannot be reused.");}
        LoginStage="reading token response";
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token=doc.RootElement.GetProperty("access_token").GetString()!;
        var parts=token.Split('.');
        if(parts.Length!=3)throw new InvalidDataException("Invalid EVE access token.");
        using var header=JsonDocument.Parse(Decode(parts[0]));using var payload=JsonDocument.Parse(Decode(parts[1]));
        LoginStage="loading EVE signing metadata";
        using var metadata=JsonDocument.Parse(await http.GetStringAsync("https://login.eveonline.com/.well-known/oauth-authorization-server",ct));
        var jwks=new Uri(metadata.RootElement.GetProperty("jwks_uri").GetString()!);
        if(jwks.Scheme!="https"||jwks.Host!="login.eveonline.com")throw new InvalidDataException("Unexpected EVE key endpoint.");
        LoginStage="loading EVE signing keys";
        using var keys=JsonDocument.Parse(await http.GetStringAsync(jwks,ct));
        LoginStage="validating token signature and claims";
        var h=header.RootElement;var p=payload.RootElement;
        if(h.Str("alg")!="RS256")throw new InvalidDataException("Unexpected token algorithm.");
        var key=keys.RootElement.GetProperty("keys").EnumerateArray().FirstOrDefault(k=>k.Str("kid")==h.Str("kid")&&k.Str("kty")=="RSA");
        if(key.ValueKind==JsonValueKind.Undefined)throw new InvalidDataException("EVE signing key unavailable.");
        using var rsa=RSA.Create();rsa.ImportParameters(new(){Modulus=Decode(key.Str("n")),Exponent=Decode(key.Str("e"))});
        var issuer=p.Str("iss").TrimEnd('/');
        var audiences=ClaimStrings(p,"aud");
        if(!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)
            ||(issuer!="https://login.eveonline.com"&&issuer!="login.eveonline.com")
            ||!audiences.Contains(clientId)||!audiences.Contains("EVE Online")
            ||p.Number("exp")<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            ||!p.Str("sub").StartsWith("CHARACTER:EVE:",StringComparison.Ordinal)
            ||!ClaimStrings(p,"scp",true).Contains(Scope))
            throw new InvalidDataException("EVE token verification failed.");
        ct.ThrowIfCancellationRequested();
        var nextRefresh=doc.RootElement.GetProperty("refresh_token").GetString();
        if(string.IsNullOrWhiteSpace(nextRefresh))throw new InvalidDataException("EVE response did not include a refresh token.");
        access=token;refresh=nextRefresh;
        expires=DateTimeOffset.FromUnixTimeSeconds(p.Number("exp"));CharacterName=p.Str("name");LoginStage="connected";
    }
    public async Task<KnownStructure> Lookup(long id,CancellationToken ct)
    {
        if(id<=0)throw new InvalidDataException("Enter a positive structure ID.");
        if(!Connected)throw new InvalidDataException("Connect an EVE character first.");
        if(DateTimeOffset.UtcNow<backoff)throw new InvalidDataException("ESI requested a pause. Try again in a minute.");
        if(expires<DateTimeOffset.UtcNow.AddMinutes(1))await Tokens(new(){{"grant_type","refresh_token"},{"refresh_token",refresh},{"client_id",clientId}},ct);
        using var request=new HttpRequestMessage(HttpMethod.Get,$"https://esi.evetech.net/latest/universe/structures/{id}/");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        using var response=await http.SendAsync(request,ct);
        if((int)response.StatusCode is 420 or 429 or 503){backoff=DateTimeOffset.UtcNow+(response.Headers.RetryAfter?.Delta??TimeSpan.FromMinutes(1));throw new InvalidDataException("ESI rate limited this request. Try again later.");}
        if(response.StatusCode==HttpStatusCode.Forbidden)throw new InvalidDataException("This character cannot query that structure (403). Access may have changed.");
        if(response.StatusCode==HttpStatusCode.NotFound)throw new InvalidDataException("Structure not found (404). Check its ID; it may no longer exist.");
        if(response.StatusCode==HttpStatusCode.Unauthorized){Disconnect();throw new InvalidDataException("Authorization expired. Connect again.");}
        response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var d=doc.RootElement;
        return new(id,d.Number("solar_system_id"),d.Number("type_id"),d.Str("name"),DateTimeOffset.UtcNow,true);
    }
    public void Disconnect(){access="";refresh="";CharacterName="";expires=default;}
    public void Dispose(){Disconnect();http.Dispose();}
}
