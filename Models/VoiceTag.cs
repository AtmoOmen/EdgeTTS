using Newtonsoft.Json;

namespace EdgeTTS.Models;

public class VoiceTag
{
    [JsonProperty("ContentCategories")]
    public List<string> ContentCategories { get; set; }

    [JsonProperty("VoicePersonalities")]
    public List<string> VoicePersonalities { get; set; }
}
