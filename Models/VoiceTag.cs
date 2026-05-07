using System.Text.Json.Serialization;

namespace EdgeTTS.Models;

public class VoiceTag
{
    [JsonPropertyName("ContentCategories")] public List<string> ContentCategories { get; set; }

    [JsonPropertyName("VoicePersonalities")] public List<string> VoicePersonalities { get; set; }
}
