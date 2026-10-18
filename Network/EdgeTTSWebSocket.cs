using System.Net.Sockets;
using System.Net.WebSockets;

namespace EdgeTTS.Network;

internal static class EdgeTTSWebSocket
{
    internal const string CHROMIUM_FULL_VERSION = "143.0.3650.75";
    internal const string SEC_MS_GEC_VERSION    = $"2-{CHROMIUM_FULL_VERSION}";

    internal static readonly string ChromiumMajorVersion = CHROMIUM_FULL_VERSION.Split('.')[0];

    internal static readonly string UserAgent =
        $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{ChromiumMajorVersion}.0.0.0 Safari/537.36 Edg/{ChromiumMajorVersion}.0.0.0";

    internal static readonly string ClientHints =
        $"\" Not;A Brand\";v=\"99\", \"Microsoft Edge\";v=\"{ChromiumMajorVersion}\", \"Chromium\";v=\"{ChromiumMajorVersion}\"";

    private const string WSS_URL =
        $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken={SecMSGEC.TRUSTED_CLIENT_TOKEN}";

    public static async Task<WebSocket> CreateWebSocketAsync
    (
        CancellationToken cancellationToken
    )
    {
        var ws = SystemClientWebSocket.CreateClientWebSocket();
        ConfigureWebSocket(ws);
        await ws.ConnectAsync(new($"{WSS_URL}&Sec-MS-GEC={SecMSGEC.Get()}&Sec-MS-GEC-Version={SEC_MS_GEC_VERSION}"), cancellationToken)
                .ConfigureAwait(false);
        return ws;
    }

    private static void ConfigureWebSocket
    (
        WebSocket ws
    )
    {
        dynamic options = ws switch
        {
            ClientWebSocket clientWs                                => clientWs.Options,
            System.Net.WebSockets.Managed.ClientWebSocket managedWs => managedWs.Options,
            _                                                       => throw new ArgumentException("Unsupported WebSocket type")
        };

        options.SetRequestHeader("Accept-Encoding",  "gzip, deflate, br");
        options.SetRequestHeader("Cache-Control",    "no-cache");
        options.SetRequestHeader("Pragma",           "no-cache");
        options.SetRequestHeader("User-Agent",       UserAgent);
        options.SetRequestHeader("Accept-Language",  "en-US,en;q=0.9");
        options.SetRequestHeader("Origin",           "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        options.SetRequestHeader("Sec-CH-UA",        ClientHints);
        options.SetRequestHeader("Sec-CH-UA-Mobile", "?0");
        options.SetRequestHeader("Accept",           "*/*");
        options.SetRequestHeader("Sec-Fetch-Site",   "none");
        options.SetRequestHeader("Sec-Fetch-Mode",   "cors");
        options.SetRequestHeader("Sec-Fetch-Dest",   "empty");
    }

    public static bool IsConnectionResetError
    (
        Exception? ex
    ) =>
        ex?.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset } ||
        ex is SocketException { SocketErrorCode: SocketError.ConnectionReset };
}
