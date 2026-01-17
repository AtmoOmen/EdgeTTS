using System.Globalization;
using Newtonsoft.Json;

namespace EdgeTTS.Models;

public class VoiceInfo
{
    private static readonly Dictionary<string, Dictionary<string, string>> GenderMap = new()
    {
        ["zh"] = new() { ["Male"] = "男", ["Female"]  = "女" },
        ["ja"] = new() { ["Male"] = "男性", ["Female"] = "女性" },
        ["ko"] = new() { ["Male"] = "남성", ["Female"] = "여성" }
    };

    [JsonProperty("Name")]           public string   Name           { get; set; }
    [JsonProperty("ShortName")]      public string   ShortName      { get; set; }
    [JsonProperty("Gender")]         public string   Gender         { get; set; }
    [JsonProperty("Locale")]         public string   Locale         { get; set; }
    [JsonProperty("SuggestedCodec")] public string   SuggestedCodec { get; set; }
    [JsonProperty("FriendlyName")]   public string   FriendlyName   { get; set; }
    [JsonProperty("Status")]         public string   Status         { get; set; }
    [JsonProperty("VoiceTag")]       public VoiceTag VoiceTag       { get; set; }

    [JsonIgnore] public CultureInfo LocaleInfo
    {
        get
        {
            if (field != null)
                return field;

            return field = CultureInfo.GetCultureInfo(Locale);
        }
    }

    [JsonIgnore] public string GenderName
    {
        get
        {
            if (field != null)
                return field;

            var currentLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

            if (GenderMap.TryGetValue(currentLang, out var langDict) && langDict.TryGetValue(Gender, out var localizedGender))
                return field = localizedGender;

            return field = Gender;
        }
    }

    public override string ToString() =>
        $"{nameof(ShortName)}: {ShortName}, " +
        $"{nameof(Gender)}: {Gender}, "       +
        $"{nameof(Locale)}: {Locale}, "       +
        $"{nameof(FriendlyName)}: {FriendlyName}";
}
