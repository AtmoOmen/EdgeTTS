using System.Net.Sockets;
using System.Net.WebSockets;

namespace EdgeTTS.Network;

internal static class EdgeTTSWebSocket
{
    private const string WSS_URL =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken=6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    public static async Task<WebSocket> CreateWebSocketAsync(CancellationToken cancellationToken)
    {
        var ws = SystemClientWebSocket.CreateClientWebSocket();
        ConfigureWebSocket(ws);
        await ws.ConnectAsync(new($"{WSS_URL}&Sec-MS-GEC={SecMSGEC.Get()}&Sec-MS-GEC-Version=1-132.0.2917.0"), cancellationToken)
                .ConfigureAwait(false);
        return ws;
    }

    private static void ConfigureWebSocket(WebSocket ws)
    {
        dynamic options = ws switch
        {
            ClientWebSocket clientWs => clientWs.Options,
            System.Net.WebSockets.Managed.ClientWebSocket managedWs => managedWs.Options,
            _ => throw new ArgumentException("Unsupported WebSocket type")
        };

        options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
        options.SetRequestHeader("Cache-Control", "no-cache");
        options.SetRequestHeader("Pragma", "no-cache");
    }

    public static bool IsConnectionResetError(Exception? ex) =>
        ex?.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset } ||
        ex is SocketException { SocketErrorCode: SocketError.ConnectionReset };
} 
