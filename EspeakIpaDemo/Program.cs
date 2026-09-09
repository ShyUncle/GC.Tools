using System;
using System.Diagnostics;
using System.Text;

namespace EspeakIpaDemo;

/// <summary>
/// eSpeak-NG 音标生成器（通过命令行调用，零第三方 NuGet 依赖）
/// 适用 Windows / Linux / macOS，需系统已安装 espeak-ng 并可在 PATH 中找到。
/// </summary>
public class EspeakPhoneticGenerator
{
    private readonly string _espeakPath;

    /// <param name="espeakPath">
    /// espeak-ng 可执行文件路径。留空则使用 PATH 查找：
    ///   - Windows 默认安装可传 @"C:\Program Files\eSpeak NG\espeak-ng.exe"
    ///   - Linux / macOS 通常直接用 "espeak-ng" 即可
    /// </param>
    public EspeakPhoneticGenerator(string? espeakPath = null)
    {
        _espeakPath = espeakPath ?? "espeak-ng";
    }

    /// <summary>
    /// 将文本转换为 IPA 国际音标
    /// </summary>
    /// <param name="text">要转换的文本</param>
    /// <param name="voice">语音/语言代码：en-us(美式)、en-gb(英式)、en(通用英语)、cmn(中文普通话)</param>
    /// <param name="tie">
    ///   0 = 默认纯音标（无连线）
    ///   1 = 使用连字线（U+0361 tie）连接双元音/塞擦音
    ///   2 = 使用 ZWJ（零宽连字）
    ///   3 = 用下划线 _ 分隔多字母音素
    /// </param>
    /// <param name="sep">自定义音素之间的分隔符；null 则不指定，使用默认（无分隔）</param>
    public string ToIpa(string text, string voice = "en-us", int tie = 0, string? sep = null)
    {
        var sb = new StringBuilder($"-v {voice} -q");
        sb.Append(tie == 0 ? " --ipa" : $" --ipa={tie}");
        if (!string.IsNullOrEmpty(sep))
            sb.Append($" --sep={EscapeForCmd(sep)}");
        sb.Append($" \"{EscapeForCmd(text)}\"");
        return RunAndReadOutput(sb.ToString());
    }

    /// <summary>
    /// 输出 eSpeak 内部音素助记符（Kirshenbaum / X-SAMPA 风格 ASCII 符号，调试用）
    /// </summary>
    public string ToPhonemes(string text, string voice = "en-us")
    {
        return RunAndReadOutput($"-v {voice} -q -x \"{EscapeForCmd(text)}\"");
    }

    /// <summary>
    /// 朗读文本（仅朗读，不返回音标）
    /// </summary>
    public void Speak(string text, string voice = "en-us", int rate = 175, int pitch = 50)
    {
        RunAndWait($"-v {voice} -s {rate} -p {pitch} \"{EscapeForCmd(text)}\"");
    }

    /// <summary>
    /// 把文本朗读结果保存为 WAV 文件
    /// </summary>
    public void SaveToWav(string text, string wavPath, string voice = "en-us", int rate = 175)
    {
        RunAndWait($"-v {voice} -s {rate} -w \"{EscapeForCmd(wavPath)}\" \"{EscapeForCmd(text)}\"");
    }

    /// <summary>
    /// 批量处理文本文件：每行一个单词/句子，输出 "原文\t音标" 到目标文件
    /// </summary>
    public void FileToIpa(string inputFile, string outputFile, string voice = "en-us", int tie = 0)
    {
        var lines = File.ReadAllLines(inputFile, Encoding.UTF8);
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) { sb.AppendLine(); continue; }
            var ipa = ToIpa(line, voice, tie).Trim();
            sb.AppendLine($"{line}\t{ipa}");
        }
        File.WriteAllText(outputFile, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>
    /// 列出所有可用语音（语言）
    /// </summary>
    public string ListVoices()
    {
        return RunAndReadOutput("--voices");
    }

    // -------------------- 内部方法 --------------------

    private string RunAndReadOutput(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _espeakPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) output.AppendLine("[ERR] " + e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return output.ToString().TrimEnd('\r', '\n');
    }

    private void RunAndWait(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = _espeakPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        process.WaitForExit();
    }

    private static string EscapeForCmd(string text)
        => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>
/// 演示入口
/// </summary>
class Program
{
    static void Main()
    {
        // Windows 如果未将 espeak-ng 加入 PATH，请取消注释并改成你的安装路径：
        var gen = new EspeakPhoneticGenerator(@"C:\Program Files\eSpeak NG\espeak-ng.exe");
        // var gen = new EspeakPhoneticGenerator();
       // Console.WriteLine(gen.ListVoices());
        Console.OutputEncoding = Encoding.UTF8; 
        // 1) 单词 / 句子 → IPA 音标
        Console.WriteLine("=== 美式英语 IPA (en-us) ===");
        Console.WriteLine(gen.ToIpa("recycle paper/plastic bottles/cans")); 
        Console.WriteLine("\n=== 英式英语 IPA (en-gb) ===");
        Console.WriteLine(gen.ToIpa("hello world", voice: "en-gb"));

        Console.WriteLine("\n=== 中文普通话（拼音式输出）===");
        Console.WriteLine(gen.ToIpa("你好世界", voice: "cmn"));

        // 2) 带连字线的 IPA（双元音更美观）
        Console.WriteLine("\n=== 带连字线的 IPA (--ipa=1) ===");
        Console.WriteLine(gen.ToIpa("nice to meet you", tie: 1));

        // 3) 自定义分隔符（每个音素之间用空格隔开，便于逐音素展示）
        Console.WriteLine("\n=== 音素间加空格 (--sep=' ') ===");
        Console.WriteLine(gen.ToIpa("doctor", sep: " "));

        // 4) 内部音素助记符（做 TTS 调试用）
        Console.WriteLine("\n=== eSpeak 内部音素 (-x) ===");
        Console.WriteLine(gen.ToPhonemes("hello"));

        // 5) 保存朗读为 WAV
         //gen.SaveToWav("an Asian elephant", @"hello.wav",voice: "en-gb", rate:120);

        // 6) 批量文件示例：准备 words.txt，每行一个单词，生成 words_ipa.txt
        // gen.FileToIpa("words.txt", "words_ipa.txt");
    }
}
