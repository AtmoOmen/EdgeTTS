主要逻辑来源于 [ACT.FoxTTS](https://github.com/Noisyfox/ACT.FoxTTS), 单独分离以便使用

音频播放使用 NAudio 实现

## 快速使用

```csharp
using EdgeTTS;

using var engine = new EdgeTTSEngine();

await engine.SpeakAsync("你好");
await engine.SpeakAsync("你好", "zh-CN-XiaoxiaoNeural", speed: 110, pitch: 105);

var audio = await engine.SynthesizeAudioAsync("你好", "zh-CN-XiaoxiaoNeural");
await File.WriteAllBytesAsync("speech.mp3", audio);
```

需要风格、角色、音频设备或发音替换时使用设置对象

```csharp
using EdgeTTS.Models;

var settings = new EdgeTTSSettings("zh-CN-XiaoxiaoNeural", speed: 110)
{
    Style       = "cheerful",
    StyleDegree = 120,
    DeviceID    = -1,
    Volume      = 80
};

await engine.SpeakAsync("你好", settings, cancellationToken);
```

缓存目录、声音文件目录和日志均为可选配置，声音文件缺失时自动读取程序集内置数据

```csharp
using var engine = new EdgeTTSEngine
(
    cacheFolder: "audio-cache",
    logHandler: Console.WriteLine
);
```
