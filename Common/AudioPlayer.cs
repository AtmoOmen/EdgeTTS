using NAudio.Wave;

namespace EdgeTTS.Common;

public class AudioPlayer : IAsyncDisposable
{
    private readonly IWavePlayer waveOut;
    private readonly AudioFileReader audioFile;
    private readonly TaskCompletionSource<bool> playbackStarted;
    private bool isDisposed;

    public event EventHandler<PlayStateChangedEventArgs>? PlayStateChanged;

    private AudioPlayer(string filePath, int audioDeviceID = -1)
    {
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
        playbackStarted = new TaskCompletionSource<bool>();
    }

    public bool IsPlaying => waveOut.PlaybackState == PlaybackState.Playing;

    public TimeSpan CurrentPosition => audioFile.CurrentTime;

    public TimeSpan Duration => audioFile.TotalTime;

    private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        PlayStateChanged?.Invoke(this, new PlayStateChangedEventArgs(WMPPlayState.Stopped));
        playbackStarted.TrySetResult(false);
    }

    public static async Task PlayAudioAsync(string filePath, int volume = 100, int audioDeviceID = -1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Path is null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found", filePath);

        await using var player = new AudioPlayer(filePath, audioDeviceID);
        player.SetVolume(volume);
        await player.PlayInternalAsync(cancellationToken).ConfigureAwait(false);
    }

    private void SetVolume(int volume)
    {
        var normalizedVolume = Math.Clamp(volume, 0, 100) / 50f;
        audioFile.Volume = normalizedVolume;
    }

    private async Task PlayInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            waveOut.Init(audioFile);
            waveOut.Play();
            PlayStateChanged?.Invoke(this, new PlayStateChangedEventArgs(WMPPlayState.Playing));
            playbackStarted.TrySetResult(true);

            // 等待音频播放完成或取消
            while (IsPlaying && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不需要处理
        }
        finally
        {
            waveOut.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;

        waveOut.Stop();
        waveOut.Dispose();
        audioFile.Dispose();

        isDisposed = true;

        await Task.CompletedTask;
    }
}

public class PlayStateChangedEventArgs(WMPPlayState playState) : EventArgs
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
