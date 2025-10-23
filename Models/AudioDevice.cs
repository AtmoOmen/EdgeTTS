namespace EdgeTTS.Models;

public class AudioDevice(int id, string name)
{
    public int    ID   { get; } = id;
    public string Name { get; } = name;
}
