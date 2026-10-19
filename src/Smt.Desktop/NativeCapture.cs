using System.Runtime.InteropServices;
using System.Text.Json;
namespace Smt.Desktop;
public static class NativeCapture
{
    [DllImport("LocalCapture",EntryPoint="smt_request")] private static extern void Request([MarshalAs(UnmanagedType.LPUTF8Str)] string json);
    [DllImport("LocalCapture",EntryPoint="smt_result")] private static extern IntPtr Result();
    [DllImport("LocalCapture",EntryPoint="smt_free")] private static extern void Free(IntPtr pointer);
    private static readonly SemaphoreSlim gate=new(1);
    private static bool timedOut;
    public static async Task<JsonElement> Call(object args)
    {
        if(!OperatingSystem.IsMacOSVersionAtLeast(14))throw new InvalidOperationException("Screen monitoring requires macOS 14 or newer.");
        await gate.WaitAsync();
        try
        {
            if(timedOut)throw new InvalidOperationException("Capture timed out; restart SMT4Mac before monitoring again.");
            Request(JsonSerializer.Serialize(args));
            for(int i=0;i<300;i++)
            {
                await Task.Delay(100);var p=Result();if(p==IntPtr.Zero)continue;
                string json;try{json=Marshal.PtrToStringUTF8(p)!;}finally{Free(p);}
                var value=JsonSerializer.Deserialize<JsonElement>(json);
                if(value.TryGetProperty("error",out var e))throw new InvalidOperationException(e.GetString());
                return value;
            }
            timedOut=true;throw new TimeoutException("Screen capture did not respond. Check Screen Recording permission and restart SMT4Mac.");
        }
        finally{gate.Release();}
    }
}
