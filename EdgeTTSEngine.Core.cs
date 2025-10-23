namespace EdgeTTS;

public sealed partial class EdgeTTSEngine : IDisposable
{
    public bool IsDisposed { get; private set; }

    public required string         CacheFolder { get; init; }
    public required string         VoiceFolder { get; init; }
    public required Action<string> LogHandler  { get; init; }

    private readonly CancellationTokenSource cancelSource = new();

    public void Dispose()
    {
        if (IsDisposed) return;

        cancelSource.Cancel();
        cancelSource.Dispose();

        IsDisposed = true;
    }
}
