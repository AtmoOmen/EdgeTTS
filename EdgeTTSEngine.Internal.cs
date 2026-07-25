using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using EdgeTTS.Common;
using EdgeTTS.Models;
using EdgeTTS.Network;

namespace EdgeTTS;

public sealed partial class EdgeTTSEngine
{
    private readonly ConcurrentDictionary<AudioPlayer, byte> activePlayers = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> cacheLocks = new(StringComparer.OrdinalIgnoreCase);
    private          CancellationTokenSource                 cancelSource  = new();

    private void Log(string message) =>
        LogHandler?.Invoke($"[EdgeTTS] {message}");

    private void CancelAndRenew()
    {
        var newSource = new CancellationTokenSource();
        var oldSource = Interlocked.Exchange(ref cancelSource, newSource);

        try
        {
            oldSource.Cancel();
        }
        catch
        {
            // ignored
        }
        finally
        {
            oldSource.Dispose();
        }
    }

    private async Task<string> GetOrCreateAudioFileAsync(string text, EdgeTTSSettings settings, CancellationToken cancellationToken)
    {
        text = SanitizeString(text, settings);

        var hash      = ComputeHash($"EdgeTTS.{text}.{settings}")[..10];
        var cacheFile = Path.Combine(CacheFolder, $"{hash}.mp3");
        var cacheLock = cacheLocks.GetOrAdd(cacheFile, static _ => new SemaphoreSlim(1, 1));

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(cacheFile))
            {
                Log("使用缓存的语音文件");
                return cacheFile;
            }

            Directory.CreateDirectory(CacheFolder);
            Log("开始合成语音");

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var content = await SynthesizeWithRetryAsync(settings, text, cancellationToken).ConfigureAwait(false);
            if (content.Length == 0)
                throw new IOException("语音合成返回空音频");

            var temporaryFile = $"{cacheFile}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryFile, content, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryFile, cacheFile, true);

                stopWatch.Stop();
                Log($"语音合成完成, 耗时: {stopWatch.ElapsedMilliseconds:F2}ms");
                Log($"已将语音保存到缓存文件: {cacheFile}");
            }
            finally
            {
                if (File.Exists(temporaryFile))
                    File.Delete(temporaryFile);
            }

            return cacheFile;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private static string SanitizeString(string text, EdgeTTSSettings settings)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        foreach (var (word, phoneme) in settings.PhonemeReplacements)
            text = text.Replace(word, phoneme);

        return SecurityElement.Escape(text.Replace('：', ':')) ?? string.Empty;
    }

    private async Task<byte[]> SynthesizeWithRetryAsync(EdgeTTSSettings settings, string text, CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var retry = 0; retry < 10; retry++)
            try
            {
                using var ws = await EdgeTTSWebSocket.CreateWebSocketAsync(cancellationToken).ConfigureAwait(false);
                return await AzureWSSynthesiser.SynthesisAsync
                       (
                           ws,
                           cancellationToken,
                           text,
                           settings.Speed,
                           settings.Pitch,
                           100,
                           settings.Voice,
                           settings.Style,
                           settings.StyleDegree,
                           settings.Role,
                           settings.ContentCategories,
                           settings.VoicePersonalities
                       )
                                               .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientSynthesisError(ex))
            {
                lastException = ex;
                if (retry == 9)
                    break;

                Log($"语音合成失败, 正在重试 ({retry + 1}/10): {ex.Message}");
                await Task.Delay(1000 * (retry + 1), cancellationToken).ConfigureAwait(false);
            }

        throw new IOException("语音合成失败, 已达到最大重试次数", lastException);
    }

    private static bool MatchesVoiceTag(VoiceTag? voiceTag, VoiceTag filter)
    {
        if (voiceTag == null)
            return false;

        var filterCategories     = filter.ContentCategories ?? [];
        var filterPersonalities  = filter.VoicePersonalities ?? [];
        var voiceCategories      = voiceTag.ContentCategories ?? [];
        var voicePersonalities   = voiceTag.VoicePersonalities ?? [];
        var categoryMatched      = filterCategories.Count == 0 ||
                                   filterCategories.Any(category =>
                                       voiceCategories.Any(value => string.Equals(value, category, StringComparison.OrdinalIgnoreCase)));
        var personalityMatched   = filterPersonalities.Count == 0 ||
                                   filterPersonalities.Any(personality =>
                                       voicePersonalities.Any(value => string.Equals(value, personality, StringComparison.OrdinalIgnoreCase)));

        return categoryMatched && personalityMatched;
    }

    private static bool IsTransientSynthesisError(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is IOException or WebSocketException)
                return true;
        }

        return false;
    }

    private static string ComputeHash(string input) =>
        SHA1.HashData(Encoding.UTF8.GetBytes(input)).ToBase36String();

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(IsDisposed, typeof(EdgeTTSEngine));
}
