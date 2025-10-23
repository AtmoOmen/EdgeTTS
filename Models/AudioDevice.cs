namespace EdgeTTS.Models;

public class AudioDevice(int id, string name)
{
    public int    Id   { get; } = id;
    public string Name { get; } = name;
}
