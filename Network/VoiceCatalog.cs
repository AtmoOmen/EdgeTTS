using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using EdgeTTS.Models;

namespace EdgeTTS.Network;

internal static class VoiceCatalog
{
    private const string VOICE_URL = "https://api.msedgeservices.com/tts/cognitiveservices/voices/list";

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public static async Task<VoiceInfo[]> FetchAsync
    (
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"{VOICE_URL}?Ocp-Apim-Subscription-Key={SecMSGEC.TRUSTED_CLIENT_TOKEN}" +
            $"&Sec-MS-GEC={SecMSGEC.Get()}&Sec-MS-GEC-Version={EdgeTTSWebSocket.SEC_MS_GEC_VERSION}"
        );
        ConfigureHeaders(request);

        using var response = await Client.SendAsync
                                         (
                                             request,
                                             HttpCompletionOption.ResponseHeadersRead,
                                             cancellationToken
                                         )
                                         .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var voices = await JsonSerializer.DeserializeAsync<VoiceInfo[]>(stream, (JsonSerializerOptions?)null, cancellationToken)
                                         .ConfigureAwait(false);

        return voices ?? throw new InvalidOperationException("语音列表响应无法解析");
    }

    private static void ConfigureHeaders
    (
        HttpRequestMessage request
    )
    {
        var headers = request.Headers;

        headers.TryAddWithoutValidation("User-Agent",       EdgeTTSWebSocket.UserAgent);
        headers.TryAddWithoutValidation("Accept-Language",  "en-US,en;q=0.9");
        headers.TryAddWithoutValidation("Origin",           "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        headers.TryAddWithoutValidation("Sec-CH-UA",        EdgeTTSWebSocket.ClientHints);
        headers.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
        headers.TryAddWithoutValidation("Accept",           "*/*");
        headers.TryAddWithoutValidation("Sec-Fetch-Site",   "none");
        headers.TryAddWithoutValidation("Sec-Fetch-Mode",   "cors");
        headers.TryAddWithoutValidation("Sec-Fetch-Dest",   "empty");
        headers.TryAddWithoutValidation("Cookie",           $"muid={RandomNumberGenerator.GetHexString(32)};");
    }
}
