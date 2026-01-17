using System.Net.WebSockets;
using System.Text;

namespace EdgeTTS.Network;

internal static class AzureWSSynthesiser
{
    private const int WEBSOCKET_TIMEOUT_MS = 15000;
    private const int BUFFER_SIZE          = 4096;
    private const int MAX_RETRIES          = 9;
    private const int RETRY_DELAY_MS       = 1000;

    private static class PathConstants
    {
        public const string SPEECH_CONFIG = "Path:speech.config";
        public const string SSML          = "Path:ssml";
        public const string TURN_START    = "Path:turn.start";
        public const string TURN_END      = "Path:turn.end";
        public const string AUDIO         = "Path:audio";
    }

    private enum ProtocolState
    {
        NotStarted,
        TurnStarted,
        Streaming
    }

    public static async Task<byte[]> SynthesisAsync
    (
        WebSocket         ws,
        CancellationToken cancellationToken,
        string            text,
        int               speed,
        int               pitch,
        int               volume,
        string            voice,
        string?           style       = null,
        int               styleDegree = 100,
        string?           role        = null
    )
    {
        ArgumentNullException.ThrowIfNull(ws);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(voice);

        var        retryCount    = 0;
        Exception? lastException = null;

        while (retryCount < MAX_RETRIES)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(WEBSOCKET_TIMEOUT_MS);

                return await ExecuteSynthesisAsync
                       (
                           ws,
                           text,
                           speed,
                           pitch,
                           volume,
                           voice,
                           style,
                           styleDegree,
                           role,
                           timeoutCts.Token
                       ).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRetry(ex) && retryCount < MAX_RETRIES - 1)
            {
                lastException = ex;
                retryCount++;

                await Task.Delay(RETRY_DELAY_MS * retryCount, cancellationToken).ConfigureAwait(false);

                if (ws.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException
                    (
                        "WebSocket connection lost and needs to be re-established",
                        ex
                    );
                }
            }
        }

        throw new IOException($"Synthesis failed after {MAX_RETRIES} attempts", lastException);
    }

    private static bool ShouldRetry(Exception ex) =>
        ex switch
        {
            IOException             => true,
            WebSocketException wsEx => wsEx.WebSocketErrorCode != WebSocketError.InvalidState,
            _                       => false
        };

    private static async Task<byte[]> ExecuteSynthesisAsync
    (
        WebSocket         ws,
        string            text,
        int               speed,
        int               pitch,
        int               volume,
        string            voice,
        string?           style,
        int               styleDegree,
        string?           role,
        CancellationToken cancellationToken
    )
    {
        var requestID = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffK");

        try
        {
            if (ws.State != WebSocketState.Open)
                throw new WebSocketException(WebSocketError.InvalidState, "WebSocket connection is not open");

            using var sendLock = new SemaphoreSlim(1, 1);

            await SendWithRetryAsync
            (
                () =>
                    SendConfigurationAsync(ws, requestID, timestamp, cancellationToken),
                sendLock,
                cancellationToken
            ).ConfigureAwait(false);

            await SendWithRetryAsync
                (
                    () =>
                        SendSpeechRequestAsync
                        (
                            ws,
                            requestID,
                            timestamp,
                            text,
                            speed,
                            pitch,
                            volume,
                            voice,
                            style,
                            styleDegree,
                            role,
                            cancellationToken
                        ),
                    sendLock,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return await ReceiveAudioDataAsync(ws, requestID, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await SafeCloseWebSocketAsync(ws).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task SendWithRetryAsync
    (
        Func<Task>        sendAction,
        SemaphoreSlim     sendLock,
        CancellationToken cancellationToken
    )
    {
        var attempts = 0;

        while (attempts < 2) // 最多重试一次
        {
            try
            {
                await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                await sendAction().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is IOException or WebSocketException && attempts == 0)
            {
                attempts++;
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sendLock.Release();
            }
        }
    }

    private static async Task<byte[]> ReceiveAudioDataAsync
    (
        WebSocket         ws,
        string            requestID,
        CancellationToken cancellationToken
    )
    {
        using var buffer        = new MemoryStream();
        var       state         = ProtocolState.NotStarted;
        var       receiveBuffer = new byte[BUFFER_SIZE];
        var       messageBuffer = new List<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;

                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                    if (buffer.Length > 0 && state == ProtocolState.Streaming)
                        return buffer.ToArray();
                    throw;
                }

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Close when buffer.Length > 0 && state == ProtocolState.Streaming:
                        return buffer.ToArray();
                    case WebSocketMessageType.Close:
                        throw new IOException("Connection closed unexpectedly");
                    case WebSocketMessageType.Text:
                    {
                        var message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                        state = await HandleTextMessageAsync(message, requestID, state).ConfigureAwait(false);

                        if (state == ProtocolState.Streaming && message.Contains(PathConstants.TURN_END))
                            return buffer.ToArray();

                        break;
                    }
                    case WebSocketMessageType.Binary:
                    {
                        messageBuffer.AddRange(new ArraySegment<byte>(receiveBuffer, 0, result.Count));

                        if (result.EndOfMessage)
                        {
                            await HandleBinaryMessageAsync(messageBuffer.ToArray(), requestID, buffer).ConfigureAwait(false);
                            state = ProtocolState.Streaming;
                            messageBuffer.Clear();
                        }

                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            if (buffer.Length > 0 && state == ProtocolState.Streaming)
                return buffer.ToArray();

            throw;
        }

        throw new OperationCanceledException();
    }

    private static async Task SendWebSocketTextAsync
    (
        WebSocket         ws,
        string            message,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (ws.State != WebSocketState.Open)
                throw new WebSocketException(WebSocketError.InvalidState);

            var buffer  = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);

            const int CHUNK_SIZE = 4096;

            for (var i = 0; i < segment.Count; i += CHUNK_SIZE)
            {
                var size         = Math.Min(CHUNK_SIZE, segment.Count - i);
                var endOfMessage = i + size >= segment.Count;

                await ws.SendAsync
                (
                    new ArraySegment<byte>(segment.Array!, segment.Offset + i, size),
                    WebSocketMessageType.Text,
                    endOfMessage,
                    cancellationToken
                ).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely, ex);
        }
    }

    private static async Task SendConfigurationAsync
    (
        WebSocket         ws,
        string            requestID,
        string            timestamp,
        CancellationToken cancellationToken
    )
    {
        var config = new StringBuilder()
                     .AppendLine(PathConstants.SPEECH_CONFIG)
                     .AppendLine($"X-RequestID:{requestID}")
                     .AppendLine($"X-Timestamp:{timestamp}")
                     .AppendLine("Content-Type:application/json")
                     .AppendLine()
                     .AppendLine
                     (
                         """{"context":{"synthesis":{"audio":{"metadataoptions":{"sentenceBoundaryEnabled":"false","wordBoundaryEnabled":"false"},"outputFormat":"audio-24khz-48kbitrate-mono-mp3"}}}}"""
                     )
                     .ToString();

        await SendWebSocketTextAsync(ws, config, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendSpeechRequestAsync
    (
        WebSocket         ws,
        string            requestID,
        string            timestamp,
        string            text,
        int               speed,
        int               pitch,
        int               volume,
        string            voice,
        string?           style,
        int               styleDegree,
        string?           role,
        CancellationToken cancellationToken
    )
    {
        var ssml = CreateSSML(text, speed, pitch, volume, voice, style, styleDegree, role);
        var request = new StringBuilder()
                      .AppendLine(PathConstants.SSML)
                      .AppendLine($"X-RequestID:{requestID}")
                      .AppendLine($"X-Timestamp:{timestamp}")
                      .AppendLine("Content-Type:application/ssml+xml")
                      .AppendLine()
                      .AppendLine(ssml)
                      .ToString();

        await SendWebSocketTextAsync(ws, request, cancellationToken).ConfigureAwait(false);
    }

    private static Task<ProtocolState> HandleTextMessageAsync
    (
        string        message,
        string        requestID,
        ProtocolState state
    )
    {
        if (!message.Contains(requestID))
            return Task.FromResult(state);

        return Task.FromResult
        (
            state switch
            {
                ProtocolState.NotStarted when message.Contains(PathConstants.TURN_START) => ProtocolState.TurnStarted,
                ProtocolState.TurnStarted when message.Contains(PathConstants.TURN_END)  => throw new IOException("Unexpected turn.end"),
                _                                                                        => state
            }
        );
    }

    private static async Task HandleBinaryMessageAsync
    (
        byte[]       data,
        string       requestID,
        MemoryStream buffer
    )
    {
        if (data.Length < 2)
            throw new IOException("Message too short");

        var headerLen = BitConverter.ToUInt16([data[1], data[0]], 0);
        if (data.Length < 2 + headerLen)
            throw new IOException("Message too short");

        var header = Encoding.UTF8.GetString(data, 2, headerLen);
        if (!header.EndsWith($"{PathConstants.AUDIO}\r\n"))
            return;

        if (!header.Contains(requestID))
            throw new IOException("Unexpected request id during streaming");

        await buffer.WriteAsync(data.AsMemory(2 + headerLen)).ConfigureAwait(false);
    }

    private static async Task SafeCloseWebSocketAsync(WebSocket ws)
    {
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await ws.CloseAsync
                (
                    WebSocketCloseStatus.NormalClosure,
                    "Synthesis completed",
                    cts.Token
                ).ConfigureAwait(false);
            }
        }
        catch
        {
            try
            {
                ws.Abort();
            }
            catch
            {
                /* ignored */
            }
        }
    }

    private static string CreateSSML
    (
        string  text,
        int     speed,
        int     pitch,
        int     volume,
        string  voice,
        string? style       = null,
        int     styleDegree = 100,
        string? role        = null
    ) =>
        new StringBuilder()
            .Append("<speak xmlns=\"http://www.w3.org/2001/10/synthesis\" xmlns:mstts=\"http://www.w3.org/2001/mstts\" version=\"1.0\" xml:lang=\"en-US\">")
            .Append($"<voice name=\"{voice}\">")
            .Append($"<prosody rate=\"{speed - 100}%\" pitch=\"{(pitch - 100) / 2}%\" volume=\"{Math.Clamp(volume, 1, 100)}\">")
            .Append(BuildExpressAs(text, style, styleDegree, role))
            .Append("</prosody></voice></speak>")
            .ToString();

    private static string BuildExpressAs(string text, string? style, int styleDegree, string? role)
    {
        if (string.IsNullOrWhiteSpace(style)                            ||
            string.IsNullOrWhiteSpace(role)                             ||
            style.Equals("general", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("default", StringComparison.OrdinalIgnoreCase))
            return text;

        var sb = new StringBuilder("<mstts:express-as");

        if (!string.IsNullOrWhiteSpace(style))
        {
            sb.Append($" style=\"{style}\"")
              .Append($" styledegree=\"{Math.Max(1, styleDegree) / 100.0f}\"");
        }

        if (!string.IsNullOrWhiteSpace(role))
            sb.Append($" role=\"{role}\"");

        return sb.Append('>')
                 .Append(text)
                 .Append("</mstts:express-as>")
                 .ToString();
    }
}
