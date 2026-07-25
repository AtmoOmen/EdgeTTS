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
    ///     内容分类合成参数, 会写入语音合成请求
    /// </summary>
    public List<string> ContentCategories { get; set; } = [];

    /// <summary>
    ///     声音性格合成参数, 会写入语音合成请求
    /// </summary>
    public List<string> VoicePersonalities { get; set; } = [];

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

    public override string ToString() =>
        $"{nameof(Speed)}: {Speed}, {nameof(Pitch)}: {Pitch}, {nameof(Voice)}: {Voice}, "           +
        $"{nameof(Style)}: {Style}, {nameof(StyleDegree)}: {StyleDegree}, {nameof(Role)}: {Role}, " +
        $"{nameof(ContentCategories)}: {string.Join(",",  ContentCategories)}, "                    +
        $"{nameof(VoicePersonalities)}: {string.Join(",", VoicePersonalities)}";
}
