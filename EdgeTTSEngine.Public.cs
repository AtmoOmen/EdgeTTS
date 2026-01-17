using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EdgeTTS.Common;
using EdgeTTS.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace EdgeTTS;

public sealed partial class EdgeTTSEngine
{
    private Dictionary<string, Dictionary<string, VoiceInfo[]>>? voices;
    
    /// <summary>
    /// 所有可用的声音列表, 在首次读取时会自动调用 <see cref="ReloadVoicesData"/> 方法填充数据并缓存, 需要刷新数据请调用 <see cref="ReloadVoicesData"/>
    /// 地区名 (Locale) - 性别 (Male / Female) - 声音
    /// <seealso cref="VoiceFolder"/>
    /// </summary>
    public Dictionary<string, Dictionary<string, VoiceInfo[]>> Voices
    {
        get
        {
            if (voices != null)
                return voices;

            return voices = ReloadVoicesData();
        }
    }

    private Dictionary<int, AudioDevice>? audioDevices;

    /// <summary>
    /// 所有可用的音频设备列表, 在首次读取时会自动调用 <see cref="ReloadAudioDevicesData"/> 方法填充数据并缓存, 需要刷新数据请调用 <see cref="ReloadAudioDevicesData"/>
    /// </summary>
    public Dictionary<int, AudioDevice> AudioDevices
    {
        get
        {
            if (audioDevices != null)
                return audioDevices;
            
            return audioDevices = ReloadAudioDevicesData();
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
        _ = Task.Run(async () =>
        {
            try
            {
                await SpeakAsync(text, settings).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log($"语音合成任务异常: {ex.Message}");
            }
        }, cancelSource.Token).ConfigureAwait(false);
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
    /// 获取系统所有可用的音频输出设备, 调用后 <see cref="AudioDevices"/> 的数据也会被刷新
    /// </summary>
    /// <returns>音频设备列表</returns>
    public Dictionary<int, AudioDevice> ReloadAudioDevicesData()
    {
        var devices = new Dictionary<int, AudioDevice>();
        
        try
        {
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                devices.TryAdd(i, new(i, capabilities.ProductName));
            }
            
            if (devices.Count == 0)
            {
                using var enumerator    = new MMDeviceEnumerator();
                var       outputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                
                for (var i = 0; i < outputDevices.Count; i++)
                {
                    var device = outputDevices[i];
                    devices.TryAdd(i, new(i, device.FriendlyName));
                }
            }
        }
        catch
        {
            devices.TryAdd(-1, new(-1, "默认音频设备"));
        }
        
        return audioDevices = devices;
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
    /// 重新从 voices.json 文件中读取声音数据, 调用后 <see cref="Voices"/> 的数据也会被刷新
    /// <seealso cref="VoiceFolder"/>
    /// </summary>
    /// <returns>声音列表</returns>
    public Dictionary<string, Dictionary<string, VoiceInfo[]>> ReloadVoicesData()
    {
        try
        {
            var jsonPath = Path.Combine(VoiceFolder, "voices.json");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"语音配置文件未找到: {jsonPath}");

            var jsonContent = File.ReadAllText(jsonPath);
            var voiceData   = JsonSerializer.Deserialize<VoiceInfo[]>(jsonContent);
            
            if (voiceData == null)
                throw new InvalidOperationException("无法解析语音配置文件");

            return voices = voiceData.OrderByDescending(x =>
                                         {
                                             if (x.LocaleInfo.Name == CultureInfo.CurrentUICulture.Name)
                                                 return 2;

                                             if (x.LocaleInfo.Parent.TwoLetterISOLanguageName == CultureInfo.CurrentUICulture.Parent.TwoLetterISOLanguageName)
                                                 return 1;
                                             
                                             return 0;
                                         }
                                     )
                                     .GroupBy(x => x.LocaleInfo.DisplayName)
                                     .ToDictionary
                                     (
                                         x => x.Key,
                                         x => x.GroupBy(d => d.GenderName)
                                               .ToDictionary(d => d.Key, d => d.ToArray())
                                     );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载语音配置失败: {ex.Message}", ex);
        }
    }
}
