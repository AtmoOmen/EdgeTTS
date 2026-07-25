using System.Text.Json.Serialization;

namespace EdgeTTS.Models;

/// <summary>
/// 声音目录中的分类和性格标签
/// </summary>
public class VoiceTag
{
    /// <summary>
    /// 内容分类
    /// </summary>
    [JsonPropertyName("ContentCategories")] public List<string> ContentCategories { get; set; } = [];

    /// <summary>
    /// 声音性格
    /// </summary>
    [JsonPropertyName("VoicePersonalities")] public List<string> VoicePersonalities { get; set; } = [];

    [JsonPropertyName("Styles")] public List<string> Styles { get; set; } = [];

    [JsonPropertyName("Roles")] public List<string> Roles { get; set; } = [];

    public override string ToString() =>
        $"{nameof(ContentCategories)}: {string.Join(",", ContentCategories ?? [])}, " +
        $"{nameof(VoicePersonalities)}: {string.Join(",", VoicePersonalities ?? [])}";
}
