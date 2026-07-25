using System.Text.Json.Serialization;

namespace EdgeTTS.Models;

/// <summary>
/// 声音目录中的风格和角色标签
/// </summary>
public class VoiceTag
{
    [JsonPropertyName("Styles")] public List<string> Styles { get; set; } = [];

    [JsonPropertyName("Roles")] public List<string> Roles { get; set; } = [];
}
