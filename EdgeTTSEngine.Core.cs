namespace EdgeTTS;

public sealed partial class EdgeTTSEngine : IDisposable
{
    public EdgeTTSEngine
    (
        string?         cacheFolder = null,
        string?         voiceFolder = null,
        Action<string>? logHandler  = null
    )
    {
        if (!string.IsNullOrWhiteSpace(cacheFolder))
            CacheFolder = cacheFolder;

        if (!string.IsNullOrWhiteSpace(voiceFolder))
            VoiceFolder = voiceFolder;

        LogHandler = logHandler;
    }

    public bool IsDisposed { get; private set; }

    public string         CacheFolder { get; init; } = Path.Combine
    (
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EdgeTTSCache"
    );
    public string         VoiceFolder { get; init; } = AppContext.BaseDirectory;
    public Action<string>? LogHandler  { get; init; }

    public void Dispose()
    {
        if (IsDisposed) return;

        IsDisposed = true;

        foreach (var player in activePlayers.Keys)
            player.Stop();

        cancelSource.Cancel();
        cancelSource.Dispose();
    }
}
