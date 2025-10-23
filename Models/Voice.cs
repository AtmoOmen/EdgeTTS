namespace EdgeTTS.Models;

public class Voice(string value, string displayName)
{
    public string Value       { get; } = value;
    public string DisplayName { get; } = displayName;

    public override string ToString() => 
        $"{nameof(Value)}: {Value}, {nameof(DisplayName)}: {DisplayName}";
}
