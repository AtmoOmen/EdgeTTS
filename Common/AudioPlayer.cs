using NAudio.Wave;

namespace EdgeTTS.Common;

public sealed class AudioPlayer : IDisposable, IAsyncDisposable
{
    private readonly AudioFileReader            audioFile;
    private readonly TaskCompletionSource<bool> playbackStarted;
    private readonly IWavePlayer                waveOut;
    private          bool                       isDisposed;

    public AudioPlayer(string filePath, int audioDeviceID = -1)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Path is null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found", filePath);

        audioFile = new AudioFileReader(filePath);

        if (audioDeviceID >= 0 && audioDeviceID < WaveOut.DeviceCount)
        {
            try
            {
                waveOut = new WaveOutEvent { DeviceNumber = audioDeviceID };
            }
            catch
            {
                // 优先使用 DirectSound，如果不可用则回退到 WaveOut
                try
                {
                    waveOut = new DirectSoundOut();
                }
                catch
                {
                    waveOut = new WaveOutEvent();
                }
            }
        }
        else
        {
            // 优先使用 DirectSound，如果不可用则回退到 WaveOut
            try
            {
                waveOut = new DirectSoundOut();
            }
            catch
            {
                waveOut = new WaveOutEvent();
            }
        }

        waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
        playbackStarted         =  new TaskCompletionSource<bool>();
    }

    public bool IsPlaying => waveOut.PlaybackState == PlaybackState.Playing;

    public TimeSpan CurrentPosition => audioFile.CurrentTime;

    public TimeSpan Duration => audioFile.TotalTime;

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (isDisposed) return;

        try
        {
            waveOut.Stop();
        }
        catch
        {
            // ignored
        }

        waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
        waveOut.Dispose();
        audioFile.Dispose();

        isDisposed = true;
    }

    public event EventHandler<PlayStateChangedEventArgs>? PlayStateChanged;

    private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        PlayStateChanged?.Invoke(this, new PlayStateChangedEventArgs(WMPPlayState.Stopped));
        playbackStarted.TrySetResult(false);
    }

    public static async Task PlayAudioAsync(string filePath, int volume = 100, int audioDeviceID = -1, CancellationToken cancellationToken = default)
    {
        await using var player = new AudioPlayer(filePath, audioDeviceID);
        await player.PlayAsync(volume, cancellationToken).ConfigureAwait(false);
    }

    public void Stop()
    {
        if (isDisposed) return;

        try
        {
            waveOut.Stop();
        }
        catch
        {
            // ignored
        }
    }

    public Task PlayAsync(int volume = 100, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SetVolume(volume);
        return PlayInternalAsync(cancellationToken);
    }

    private void SetVolume(int volume)
    {
        var normalizedVolume = Math.Clamp(volume, 0, 100) / 100f;
        audioFile.Volume = normalizedVolume;
    }

    private async Task PlayInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            waveOut.Init(audioFile);
            cancellationToken.ThrowIfCancellationRequested();
            waveOut.Play();
            PlayStateChanged?.Invoke(this, new PlayStateChangedEventArgs(WMPPlayState.Playing));
            playbackStarted.TrySetResult(true);

            // 等待音频播放完成或取消
            while (IsPlaying && !cancellationToken.IsCancellationRequested)
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            Stop();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(isDisposed, typeof(AudioPlayer));
}

public class PlayStateChangedEventArgs
(
    WMPPlayState playState
) : EventArgs
{
    public WMPPlayState PlayState { get; } = playState;
}

public enum WMPPlayState
{
    Undefined     = 0,
    Stopped       = 1,
    Paused        = 2,
    Playing       = 3,
    ScanForward   = 4,
    ScanBackward  = 5,
    Buffering     = 6,
    Waiting       = 7,
    MediaEnded    = 8,
    Transitioning = 9,
    Ready         = 10,
    Reconnecting  = 11,
    Last          = 12
}
