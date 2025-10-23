using System.Collections.Concurrent;
using System.Diagnostics;
using EdgeTTS.Common;
using EdgeTTS.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace EdgeTTS;

public sealed partial class EdgeTTSEngine
{
    private Voice[]? voices;
    
    /// <summary>
    /// 所有可用的语音列表
    /// </summary>
    public Voice[] Voices
    {
        get
        {
            if (voices != null)
                return voices;

            return voices = LoadVoicesFromJSON();
        } 
    }
    
    /// <summary>
    /// 同步播放指定文本的语音
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <param name="settings">语音合成设置</param>
    public void Speak(string text, EdgeTTSSettings settings)
    {
        ThrowIfDisposed();
        try
        {
            Task.Run(async () => await SpeakAsync(text, settings).ConfigureAwait(false), cancelSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    /// <summary>
    /// 异步播放指定文本的语音
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <param name="settings">语音合成设置</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task SpeakAsync(string text, EdgeTTSSettings settings)
    {
        ThrowIfDisposed();
        var audioFile = await GetOrCreateAudioFileAsync(text, settings).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(audioFile)) return;
        await AudioPlayer.PlayAudioAsync(audioFile, settings.Volume, settings.DeviceID).ConfigureAwait(false);
    }

    /// <summary>
    /// 同步缓存指定文本的音频文件
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <param name="settings">语音合成设置</param>
    public void CacheAudioFile(string text, EdgeTTSSettings settings)
    {
        ThrowIfDisposed();
        try
        {
            Task.Run(async () => await GetAudioFileAsync(text, settings).ConfigureAwait(false), cancelSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    /// <summary>
    /// 获取指定文本的音频文件路径
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <param name="settings">语音合成设置</param>
    /// <returns>音频文件的完整路径</returns>
    public async Task<string> GetAudioFileAsync(string text, EdgeTTSSettings settings)
    {
        ThrowIfDisposed();
        var audioFile = await GetOrCreateAudioFileAsync(text, settings);
        return audioFile;
    }

    /// <summary>
    /// 同步批量缓存多个文本的音频文件
    /// </summary>
    /// <param name="texts">要转换为语音的文本集合</param>
    /// <param name="settings">语音合成设置</param>
    /// <param name="maxConcurrency">最大并行处理数量，默认为4</param>
    /// <param name="progressCallback">进度回调函数，参数为已完成数量和总数量</param>
    public void CacheAudioFiles(
        IEnumerable<string> texts,
        EdgeTTSSettings     settings,
        int                 maxConcurrency   = 4,
        Action<int, int>?   progressCallback = null)
    {
        ThrowIfDisposed();
        try
        {
            Task.Run(async () => await GetAudioFilesAsync(texts,
                                                          settings,
                                                          maxConcurrency,
                                                          progressCallback,
                                                          cancelSource.Token)
                                     .ConfigureAwait(false), cancelSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    /// <summary>
    /// 批量获取多个文本的音频文件路径，高效率地预先合成多个文本音频
    /// </summary>
    /// <param name="texts">要转换为语音的文本集合</param>
    /// <param name="settings">语音合成设置</param>
    /// <param name="maxConcurrency">最大并行处理数量，默认为4</param>
    /// <param name="progressCallback">进度回调函数，参数为已完成数量和总数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含所有文本对应音频文件路径的字典</returns>
    public async Task<Dictionary<string, string>> GetAudioFilesAsync(
        IEnumerable<string> texts,
        EdgeTTSSettings     settings,
        int                 maxConcurrency    = 4,
        Action<int, int>?   progressCallback  = null,
        CancellationToken   cancellationToken = default)
    {
        ThrowIfDisposed();

        var textList = texts.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (textList.Count == 0) return new Dictionary<string, string>();

        var result         = new ConcurrentDictionary<string, string>();
        var completedCount = 0;

        Log($"开始批量合成 {textList.Count} 个文本的语音");
        var totalStopwatch = new Stopwatch();
        totalStopwatch.Start();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancelSource.Token);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken      = linkedCts.Token
        };

        try
        {
            await Parallel.ForEachAsync(textList, parallelOptions, async (text, _) =>
            {
                var audioFile = await GetOrCreateAudioFileAsync(text, settings).ConfigureAwait(false);
                result[text] = audioFile;
                var completed = Interlocked.Increment(ref completedCount);
                progressCallback?.Invoke(completed, textList.Count);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log("批量语音合成已取消");
            throw;
        }
        catch (Exception ex)
        {
            Log($"批量语音合成过程中发生错误: {ex.Message}");
            throw;
        }
        finally
        {
            totalStopwatch.Stop();
            Log($"批量语音合成完成，共 {completedCount}/{textList.Count} 个文本，总耗时: {totalStopwatch.ElapsedMilliseconds}ms");
        }

        return new Dictionary<string, string>(result);
    }

    /// <summary>
    /// 停止当前正在进行的语音合成或播放操作
    /// </summary>
    public void Stop()
    {
        if (!IsDisposed)
            cancelSource.Cancel();
    }
    
    /// <summary>
    /// 获取系统默认音频输出设备的ID
    /// </summary>
    /// <returns>默认音频设备ID，如果无法获取则返回-1</returns>
    public static int GetDefaultAudioDeviceID()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            
            var defaultDevice     = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var defaultDeviceName = defaultDevice.FriendlyName;
            
            for (var deviceNumber = 0; deviceNumber < WaveOut.DeviceCount; deviceNumber++)
            {
                var capabilities = WaveOut.GetCapabilities(deviceNumber);
                
                if (capabilities.ProductName.Equals(defaultDeviceName, StringComparison.OrdinalIgnoreCase) || 
                    capabilities.ProductName.Contains(defaultDeviceName, StringComparison.OrdinalIgnoreCase) || 
                    defaultDeviceName.Contains(capabilities.ProductName, StringComparison.OrdinalIgnoreCase))
                {
                    return deviceNumber;
                }
            }
            
            return WaveOut.DeviceCount > 0 ? 0 : -1;
        }
        catch
        {
            return -1;
        }
    }
    
    /// <summary>
    /// 获取系统所有可用的音频输出设备
    /// </summary>
    /// <returns>音频设备列表</returns>
    public static List<AudioDevice> GetAudioDevices()
    {
        var devices = new List<AudioDevice>();
        
        try
        {
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                devices.Add(new(i, capabilities.ProductName));
            }
            
            if (devices.Count == 0)
            {
                using var enumerator = new MMDeviceEnumerator();
                var outputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                
                for (var i = 0; i < outputDevices.Count; i++)
                {
                    var device = outputDevices[i];
                    devices.Add(new(i, device.FriendlyName));
                }
            }
        }
        catch
        {
            devices.Add(new(-1, "默认音频设备"));
        }
        
        return devices;
    }
}
