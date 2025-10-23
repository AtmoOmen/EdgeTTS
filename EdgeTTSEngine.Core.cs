using EdgeTTS.Models;

namespace EdgeTTS;

public sealed partial class EdgeTTSEngine : IDisposable
{
    public bool IsDisposed { get; private set; }

    public required string         CacheFolder { get; init; }
    public required Action<string> LogHandler  { get; init; }

    private readonly CancellationTokenSource cancelSource = new();

    /// <summary>
    /// 所有可用的语音列表
    /// </summary>
    public static readonly Voice[] Voices =
    [
        new("zh-CN-XiaoxiaoNeural", "晓晓 (中文-普通话-女)"),
        new("zh-CN-XiaoyiNeural", "晓依 (中文-普通话-女)"),
        new("zh-CN-YunjianNeural", "云健 (中文-普通话-男)"),
        new("zh-CN-YunyangNeural", "云扬 (中文-普通话-新闻-男)"),
        new("zh-CN-YunxiaNeural", "云夏 (中文-普通话-儿童-男)"),
        new("zh-CN-YunxiNeural", "云希 (中文-普通话-男)"),
        new("zh-HK-HiuGaaiNeural", "曉佳 (中文-廣東話-女)"),
        new("zh-HK-HiuMaanNeural", "曉曼 (中文-廣東話-女)"),
        new("zh-HK-WanLungNeural", "雲龍 (中文-廣東話-男)"),
        new("zh-TW-HsiaoChenNeural", "曉臻 (中文-國語-女)"),
        new("zh-TW-HsiaoYuNeural", "曉雨 (中文-國語-女)"),
        new("zh-TW-YunJheNeural", "雲哲 (中文-國語-男)"),
        new("ja-JP-NanamiNeural", "七海 (日本語-女)"),
        new("ja-JP-KeitaNeural", "庆太 (日本語-男)"),
        new("en-US-AriaNeural", "Aria (English-American-Female)"),
        new("en-US-JennyNeural", "Jenny (English-American-Female)"),
        new("en-US-AnaNeural", "Ana (English-American-Child-Female)"),
        new("en-US-MichelleNeural", "Michelle (English-American-Female)"),
        new("en-US-GuyNeural", "Guy (English-American-Male)"),
        new("en-US-ChristopherNeural", "Christopher (English-American-Male)"),
        new("en-US-EricNeural", "Eric (English-American-Male)"),
        new("en-US-RogerNeural", "Roger (English-American-Male)"),
        new("en-US-SteffanNeural", "Steffan (English-American-Male)"),
        new("en-GB-SoniaNeural", "Sonia (English-Britain-Female)"),
    ];

    public void Dispose()
    {
        if (IsDisposed) return;

        cancelSource.Cancel();
        cancelSource.Dispose();

        IsDisposed = true;
    }
}
