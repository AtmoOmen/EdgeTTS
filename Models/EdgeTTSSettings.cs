namespace EdgeTTS.Models;

/// <summary>
///     Edge TTS 合成与播放设置
/// </summary>
public class EdgeTTSSettings
{
    /// <summary>
    ///     使用默认设置
    /// </summary>
    public EdgeTTSSettings()
    {
    }

    /// <summary>
    ///     创建基础语音设置
    /// </summary>
    /// <param name="voice">声音短名称</param>
    /// <param name="speed">语速, 有效范围为 1 到 200</param>
    /// <param name="pitch">音调, 有效范围为 1 到 200</param>
    /// <param name="volume">播放音量, 有效范围为 0 到 100</param>
    /// <param name="deviceID">音频设备编号, -1 表示默认设备</param>
    public EdgeTTSSettings
    (
        string voice,
        int    speed    = 100,
        int    pitch    = 100,
        int    volume   = 100,
        int    deviceID = -1
    )
    {
        Voice    = voice;
        Speed    = speed;
        Pitch    = pitch;
        Volume   = volume;
        DeviceID = deviceID;
    }

    /// <summary>
    ///     音频设备编号, -1 表示默认设备
    /// </summary>
    public int DeviceID { get; set; } = -1;

    /// <summary>
    ///     语速, 100 表示正常语速
    /// </summary>
    public int Speed { get; set; } = 100;

    /// <summary>
    ///     音调, 100 表示正常音调
    /// </summary>
    public int Pitch { get; set; } = 100;

    /// <summary>
    ///     播放音量, 100 表示满音量
    /// </summary>
    public int Volume { get; set; } = 100;

    /// <summary>
    ///     声音短名称, 例如 zh-CN-YunyangNeural
    /// </summary>
    public string Voice { get; set; } = "zh-CN-YunyangNeural";

    /// <summary>
    ///     SSML 表达风格, 为空时不启用风格包装
    /// </summary>
    public string? Style { get; set; }

    /// <summary>
    ///     SSML 风格强度, 有效范围为 1 到 200
    /// </summary>
    public int StyleDegree { get; set; } = 100;

    /// <summary>
    ///     SSML 角色, 为空时不设置角色
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    ///     文本发音替换表
    /// </summary>
    public Dictionary<string, string> PhonemeReplacements { get; set; } = new()
    {
        ["欧米茄"]  = "欧米加",
        ["歐米茄"]  = "歐米加",
        ["要塞"]   = "要赛",
        ["拾级迷宫"] = "十级迷宫"
    };

    /// <summary>
    ///     切换到指定音色, 自动清除不兼容的 Style 和 Role
    /// </summary>
    public void SelectVoice
    (
        VoiceInfo voice
    )
    {
        Voice = voice.ShortName;
        var tag = voice.VoiceTag ?? new();

        if (tag.Styles.Count == 0 || !tag.Styles.Contains(Style ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            Style = null;

        if (tag.Roles.Count == 0 || !tag.Roles.Contains(Role ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            Role = null;
    }

    public override string ToString() =>
        $"{nameof(Speed)}: {Speed}, {nameof(Pitch)}: {Pitch}, {nameof(Voice)}: {Voice}, "           +
        $"{nameof(Style)}: {Style}, {nameof(StyleDegree)}: {StyleDegree}, {nameof(Role)}: {Role}";
}
