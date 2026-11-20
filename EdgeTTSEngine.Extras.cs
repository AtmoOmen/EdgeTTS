using System.Net;
using EdgeTTS.Network;

namespace EdgeTTS;

public sealed partial class EdgeTTSEngine
{
    private IWebProxy? proxy;

    /// <summary>合成超时时间</summary>
    public TimeSpan SynthesisTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>语音合成使用的代理，null 走系统默认</summary>
    public IWebProxy? Proxy
    {
        get => proxy;
        set
        {
            proxy                  = value;
            EdgeTTSWebSocket.Proxy = value;
            VoiceCatalog.Proxy     = value;
        }
    }

    /// <summary>停止播放并取消进行中的合成</summary>
    public void Stop()
    {
        ThrowIfDisposed();

        foreach (var player in activePlayers.Keys)
            player.Stop();

        var previous = Interlocked.Exchange(ref cancelSource, new CancellationTokenSource());
        try
        {
            previous.Cancel();
        }
        catch
        {
        }
        finally
        {
            previous.Dispose();
        }
    }
}
