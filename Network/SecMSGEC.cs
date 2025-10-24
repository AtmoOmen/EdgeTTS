using System.Security.Cryptography;
using System.Text;

namespace EdgeTTS.Network;

internal static class SecMSGEC
{
    private static string? secMSGEC;
    private static long    lastTicks;

    public static void Update()
    {
        var ticks = DateTime.Now.ToFileTimeUtc();
        var str   = $"{ticks - (ticks % 3_000_000_000)}6A5AA1D4EAFF4E9FB37E23D68491D6F4";
        var by    = SHA256.HashData(Encoding.UTF8.GetBytes(str));
        
        secMSGEC  = Convert.ToHexString(by).ToUpper();
        lastTicks = ticks;
    }

    public static string? Get()
    {
        if (DateTime.Now.ToFileTimeUtc() >= lastTicks + 3_000_000_000) 
            Update();
        
        return secMSGEC;
    }
}
