using System.Globalization;

namespace EdgeTTS.Network;

internal static class SecMSGEC
{
    internal const string TRUSTED_CLIENT_TOKEN = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    private const long WINDOW_TICKS = 3_000_000_000;

    private static string? secMSGEC;
    private static long    lastTicks;

    public static void Update()
    {
        var ticks        = DateTime.UtcNow.ToFileTimeUtc();
        var alignedTicks = ticks - (ticks % WINDOW_TICKS);

        secMSGEC  = SecMSGECHash.Compute(alignedTicks.ToString(CultureInfo.InvariantCulture) + TRUSTED_CLIENT_TOKEN);
        lastTicks = ticks;
    }

    public static string? Get()
    {
        if (DateTime.UtcNow.ToFileTimeUtc() >= lastTicks + WINDOW_TICKS)
            Update();

        return secMSGEC;
    }
}
