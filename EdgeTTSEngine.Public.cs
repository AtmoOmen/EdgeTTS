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
    private Dictionary<int, AudioDevice>?                        audioDevices;
    private Dictionary<string, Dictionary<string, VoiceInfo[]>>? voices;

    /// <summary>
    ///     所有可用的声音列表, 在首次读取时会自动调用 <see cref="ReloadVoicesData" /> 方法填充数据并缓存, 需要刷新数据请调用 <see cref="ReloadVoicesData" />
    ///     地区名 (Locale) - 性别 (Male / Female) - 声音
    ///     <seealso cref="VoiceFolder" />
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

    /// <summary>
    /// 按地区、性别和声音标签查询可用声音
    /// </summary>
    /// <param name="voiceTag">可选声音标签筛选条件</param>
    /// <param name="locale">可选地区代码, 例如 zh-CN</param>
    /// <param name="gender">可选性别, 例如 Male 或 Female</param>
    /// <returns>匹配的声音列表</returns>
    public IReadOnlyList<VoiceInfo> FindVoices
    (
        VoiceTag? voiceTag = null,
        string?   locale   = null,
        string?   gender   = null
    )
    {
        var candidates = Voices.Values
                               .SelectMany(genders => genders.Values)
                               .SelectMany(items => items)
                               .Where(voice => string.IsNullOrWhiteSpace(locale) ||
                                               string.Equals(voice.Locale, locale, StringComparison.OrdinalIgnoreCase))
                               .Where(voice => string.IsNullOrWhiteSpace(gender) ||
                                               string.Equals(voice.Gender, gender, StringComparison.OrdinalIgnoreCase));

        if (voiceTag != null)
            candidates = candidates.Where(voice => MatchesVoiceTag(voice.VoiceTag, voiceTag));

        return candidates.ToArray();
    }

    /// <summary>
    ///     所有可用的音频设备列表, 在首次读取时会自动调用 <see cref="ReloadAudioDevicesData" /> 方法填充数据并缓存, 需要刷新数据请调用
    ///     <see cref="ReloadAudioDevicesData" />
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
    ///     同步播放指定文本的语音
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <param name="settings">语音合成设置</param>
    public void Speak(string text, EdgeTTSSettings settings)
    {
        ThrowIfDisposed();
        var token = cancelSource.Token;
        _ = RunDetachedAsync(() => SpeakAsync(text, settings, token));
    }

    public void Stop()
    {
        foreach (var player in activePlayers.Keys)
            player.Stop();
    }

    public void Speak(string text) =>
        Speak(text, new EdgeTTSSettings());

    public void Speak
    (
        string text,
        string voice,
        int    speed    = 100,
        int    pitch    = 100,
        int    volume   = 100,
        int    deviceID = -1
    ) => Speak(text, CreateSettings(voice, speed, pitch, volume, deviceID));

    /// <summary>
    ///     异步播放指定文本的语音
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <param name="settings">语音合成设置</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task SpeakAsync(string text, EdgeTTSSettings settings)
    {
        ThrowIfDisposed();
        var token = cancelSource.Token;
        await SpeakAsync(text, settings, token).ConfigureAwait(false);
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default) =>
        SpeakAsync(text, new EdgeTTSSettings(), cancellationToken);

    public Task SpeakAsync
    (
        string text,
        string voice,
        int    speed                = 100,
        int    pitch                = 100,
        int    volume               = 100,
        int    deviceID             = -1,
        CancellationToken cancellationToken = default
    ) => SpeakAsync(text, CreateSettings(voice, speed, pitch, volume, deviceID), cancellationToken);

    public async Task SpeakAsync(string text, EdgeTTSSettings settings, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateSettings(settings);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelSource.Token, cancellationToken);
        var audioFile = await GetOrCreateAudioFileAsync(text, settings, linkedCts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(audioFile)) return;

        var player = new AudioPlayer(audioFile, settings.DeviceID);
        activePlayers.TryAdd(player, 0);

        try
        {
            await player.PlayAsync(settings.Volume, linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            activePlayers.TryRemove(player, out _);
            player.Dispose();
        }
    }

    public async Task<byte[]> SynthesizeAudioAsync
    (
        string text,
        EdgeTTSSettings settings,
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        ValidateSettings(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelSource.Token, cancellationToken);
        var sanitizedText = SanitizeString(text, settings);
        return await SynthesizeWithRetryAsync(settings, sanitizedText, linkedCts.Token).ConfigureAwait(false);
    }

    public Task<byte[]> SynthesizeAudioAsync(string text, CancellationToken cancellationToken = default) =>
        SynthesizeAudioAsync(text, new EdgeTTSSettings(), cancellationToken);

    public Task<byte[]> SynthesizeAudioAsync
    (
        string text,
        string voice,
        int    speed                = 100,
        int    pitch                = 100,
        CancellationToken cancellationToken = default
    ) => SynthesizeAudioAsync(text, CreateSettings(voice, speed, pitch), cancellationToken);

    /// <summary>
    ///     同步缓存指定文本的音频文件
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <param name="settings">语音合成设置</param>
    public void Synthesize(string text, EdgeTTSSettings settings)
    {
        ThrowIfDisposed();
        var token = cancelSource.Token;
        _ = RunDetachedAsync(() => SynthesizeAsync(text, settings, token));
    }

    public Task SynthesizeAsync
    (
        string text,
        EdgeTTSSettings settings,
        CancellationToken cancellationToken = default
    ) => GetAudioFileAsync(text, settings, cancellationToken);

    private async Task RunDetachedAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log("后台语音任务已取消");
        }
        catch (Exception ex)
        {
            Log($"后台语音任务异常: {ex.Message}");
        }
    }

    /// <summary>
    ///     获取指定文本的音频文件路径
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <param name="settings">语音合成设置</param>
    /// <returns>音频文件的完整路径</returns>
    public Task<string> GetAudioFileAsync(string text, EdgeTTSSettings settings) =>
        GetAudioFileAsync(text, settings, CancellationToken.None);

    public Task<string> GetAudioFileAsync(string text, CancellationToken cancellationToken = default) =>
        GetAudioFileAsync(text, new EdgeTTSSettings(), cancellationToken);

    public Task<string> GetAudioFileAsync
    (
        string text,
        string voice,
        int    speed                = 100,
        int    pitch                = 100,
        CancellationToken cancellationToken = default
    ) => GetAudioFileAsync(text, CreateSettings(voice, speed, pitch), cancellationToken);

    public async Task<string> GetAudioFileAsync
    (
        string text,
        EdgeTTSSettings settings,
        CancellationToken cancellationToken
    )
    {
        ThrowIfDisposed();
        ValidateSettings(settings);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelSource.Token, cancellationToken);
        var audioFile = await GetOrCreateAudioFileAsync(text, settings, linkedCts.Token).ConfigureAwait(false);
        return audioFile;
    }

    /// <summary>
    ///     同步批量缓存多个文本的音频文件
    /// </summary>
    /// <param name="texts">要转换为语音的文本集合</param>
    /// <param name="settings">语音合成设置</param>
    /// <param name="maxConcurrency">最大并行处理数量，默认为4</param>
    /// <param name="progressCallback">进度回调函数，参数为已完成数量和总数量</param>
    public void Synthesize
    (
        IEnumerable<string> texts,
        EdgeTTSSettings     settings,
        int                 maxConcurrency   = 4,
        Action<int, int>?   progressCallback = null
    )
    {
        ThrowIfDisposed();
        var token = cancelSource.Token;
        _ = RunDetachedAsync(() => SynthesizeAsync(texts, settings, maxConcurrency, progressCallback, token));
    }

    public Task<Dictionary<string, string>> SynthesizeAsync
    (
        IEnumerable<string> texts,
        EdgeTTSSettings     settings,
        int                 maxConcurrency    = 4,
        Action<int, int>?   progressCallback  = null,
        CancellationToken   cancellationToken = default
    ) => GetAudioFilesAsync(texts, settings, maxConcurrency, progressCallback, cancellationToken);

    private static void ValidateSettings(EdgeTTSSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Voice);

        if (settings.Speed is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(settings.Speed), "Speed must be between 1 and 200");

        if (settings.Pitch is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(settings.Pitch), "Pitch must be between 1 and 200");

        if (settings.Volume is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(settings.Volume), "Volume must be between 0 and 100");

        if (settings.StyleDegree is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(settings.StyleDegree), "StyleDegree must be between 1 and 200");

        if (settings.DeviceID < -1)
            throw new ArgumentOutOfRangeException(nameof(settings.DeviceID), "DeviceID must be -1 or greater");

        ArgumentNullException.ThrowIfNull(settings.ContentCategories);
        ArgumentNullException.ThrowIfNull(settings.VoicePersonalities);
        ArgumentNullException.ThrowIfNull(settings.PhonemeReplacements);
    }

    private static EdgeTTSSettings CreateSettings
    (
        string voice,
        int    speed,
        int    pitch,
        int    volume   = 100,
        int    deviceID = -1
    ) => new(voice, speed, pitch, volume, deviceID);

    /// <summary>
    ///     批量获取多个文本的音频文件路径，高效率地预先合成多个文本音频
    /// </summary>
    /// <param name="texts">要转换为语音的文本集合</param>
    /// <param name="settings">语音合成设置</param>
    /// <param name="maxConcurrency">最大并行处理数量，默认为4</param>
    /// <param name="progressCallback">进度回调函数，参数为已完成数量和总数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含所有文本对应音频文件路径的字典</returns>
    public async Task<Dictionary<string, string>> GetAudioFilesAsync
    (
        IEnumerable<string> texts,
        EdgeTTSSettings     settings,
        int                 maxConcurrency    = 4,
        Action<int, int>?   progressCallback  = null,
        CancellationToken   cancellationToken = default
    )
    {
        ThrowIfDisposed();
        ValidateSettings(settings);
        ArgumentNullException.ThrowIfNull(texts);

        if (maxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        var textList = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal).ToList();
        if (textList.Count == 0) return new Dictionary<string, string>();

        var result         = new ConcurrentDictionary<string, string>();
        var completedCount = 0;

        Log($"开始批量合成 {textList.Count} 个文本的语音");
        var totalStopwatch = new Stopwatch();
        totalStopwatch.Start();

        var       stopToken = cancelSource.Token;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken      = linkedCts.Token
        };

        try
        {
            var token = linkedCts.Token;
            await Parallel.ForEachAsync
            (
                textList,
                parallelOptions,
                async (text, _) =>
                {
                    var audioFile = await GetOrCreateAudioFileAsync(text, settings, token).ConfigureAwait(false);
                    result[text] = audioFile;
                    var completed = Interlocked.Increment(ref completedCount);
                    progressCallback?.Invoke(completed, textList.Count);
                }
            ).ConfigureAwait(false);
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
    ///     获取系统所有可用的音频输出设备, 调用后 <see cref="AudioDevices" /> 的数据也会被刷新
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
    ///     获取系统默认音频输出设备的ID
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

                if (capabilities.ProductName.Equals(defaultDeviceName, StringComparison.OrdinalIgnoreCase)   ||
                    capabilities.ProductName.Contains(defaultDeviceName, StringComparison.OrdinalIgnoreCase) ||
                    defaultDeviceName.Contains(capabilities.ProductName, StringComparison.OrdinalIgnoreCase))
                    return deviceNumber;
            }

            return WaveOut.DeviceCount > 0 ? 0 : -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    ///     重新从 voices.json 文件中读取声音数据, 调用后 <see cref="Voices" /> 的数据也会被刷新
    ///     <seealso cref="VoiceFolder" />
    /// </summary>
    /// <returns>声音列表</returns>
    public Dictionary<string, Dictionary<string, VoiceInfo[]>> ReloadVoicesData()
    {
        try
        {
            var jsonPath = Path.Combine(VoiceFolder, "voices.json");
            var jsonContent = File.Exists(jsonPath)
                                   ? File.ReadAllText(jsonPath)
                                   : ReadEmbeddedVoicesData();
            var voiceData   = JsonSerializer.Deserialize<VoiceInfo[]>(jsonContent);

            if (voiceData == null)
                throw new InvalidOperationException("无法解析语音配置文件");

            return voices = voiceData.OrderByDescending
                                     (x =>
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

    private static string ReadEmbeddedVoicesData()
    {
        const string RESOURCE_NAME = "EdgeTTS.voices.json";
        using var stream = typeof(EdgeTTSEngine).Assembly.GetManifestResourceStream(RESOURCE_NAME);
        if (stream == null)
            throw new FileNotFoundException($"语音配置文件未找到, 资源名: {RESOURCE_NAME}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
