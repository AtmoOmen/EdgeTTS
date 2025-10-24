using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using EdgeTTS.Common;
using EdgeTTS.Models;
using EdgeTTS.Network;

namespace EdgeTTS;

public sealed partial class EdgeTTSEngine
{
    private readonly CancellationTokenSource cancelSource = new();
    
    private void Log(string message) => 
        LogHandler?.Invoke($"[EdgeTTS] {message}");

    private async Task<string> GetOrCreateAudioFileAsync(string text, EdgeTTSSettings settings)
    {
        text = SanitizeString(text, settings);
        
        var hash      = ComputeHash($"EdgeTTS.{text}.{settings}")[..10];
        var cacheFile = Path.Combine(CacheFolder, $"{hash}.mp3");

        if (!File.Exists(cacheFile))
        {
            if (!Directory.Exists(CacheFolder))
                Directory.CreateDirectory(CacheFolder);
            
            Log("开始合成语音");
            
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            
            var content = await SynthesizeWithRetryAsync(settings, text).ConfigureAwait(false);
            if (content != null)
            {
                await File.WriteAllBytesAsync(cacheFile, content).ConfigureAwait(false);
                
                stopWatch.Stop();
                Log($"语音合成完成, 耗时: {stopWatch.ElapsedMilliseconds:F2}ms");
                Log($"已将语音保存到缓存文件: {cacheFile}");
            }
        }
        else
        {
            Log("使用缓存的语音文件");
        }

        return cacheFile;
    }
    
    private static string SanitizeString(string text, EdgeTTSSettings settings)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        
        foreach (var (word, phoneme) in settings.PhonemeReplacements)
            text = text.Replace(word, phoneme);
        
        var safeText = SecurityElement.Escape(text.Replace('：', ':'));
        return safeText;
    }

    private async Task<byte[]?> SynthesizeWithRetryAsync(EdgeTTSSettings settings, string text)
    {
        for (var retry = 0; retry < 10; retry++)
        {
            try
            {
                using var ws = await EdgeTTSWebSocket.CreateWebSocketAsync(cancelSource.Token).ConfigureAwait(false);
                return await AzureWSSynthesiser.SynthesisAsync(ws, cancelSource.Token, text, settings.Speed, settings.Pitch, 100, settings.Voice)
                                               .ConfigureAwait(false);
            }
            catch (Exception ex) when (EdgeTTSWebSocket.IsConnectionResetError(ex) && retry < 9)
            {
                Log($"语音合成失败, 正在重试 ({retry + 1}/10): {ex.Message}");
                await Task.Delay(1000 * (retry + 1)).ConfigureAwait(false);
            }
        }

        Log("语音合成失败, 已达到最大重试次数");
        return null;
    }

    private static string ComputeHash(string input) =>
        SHA1.HashData(Encoding.UTF8.GetBytes(input)).ToBase36String();

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(IsDisposed, typeof(EdgeTTSEngine));
    
    private class VoiceData
    {
        public string Value       { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
