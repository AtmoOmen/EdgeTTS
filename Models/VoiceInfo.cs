using System.Globalization;
using System.Text.Json.Serialization;

namespace EdgeTTS.Models;

public class VoiceInfo
{
    [JsonPropertyName("Name")]           public string   Name           { get; set; }
    [JsonPropertyName("ShortName")]      public string   ShortName      { get; set; }
    [JsonPropertyName("Gender")]         public string   Gender         { get; set; }
    [JsonPropertyName("Locale")]         public string   Locale         { get; set; }
    [JsonPropertyName("SuggestedCodec")] public string   SuggestedCodec { get; set; }
    [JsonPropertyName("FriendlyName")]   public string   FriendlyName   { get; set; }
    [JsonPropertyName("Status")]         public string   Status         { get; set; }
    [JsonPropertyName("VoiceTag")]       public VoiceTag VoiceTag       { get; set; }

    [JsonIgnore] public CultureInfo LocaleInfo
    {
        get
        {
            if (field != null)
                return field;

            return field = CultureInfo.GetCultureInfo(Locale);
        }
    }

    public override string ToString() =>
        $"{nameof(ShortName)}: {ShortName}, " +
        $"{nameof(Gender)}: {Gender}, "       +
        $"{nameof(Locale)}: {Locale}, "       +
        $"{nameof(FriendlyName)}: {FriendlyName}";
}
