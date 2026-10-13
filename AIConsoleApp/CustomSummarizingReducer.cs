using Microsoft.Extensions.AI;

public sealed class CustomSummarizingReducer : IChatReducer
{
    private readonly IChatClient _summarizer;
    private readonly int _keepRecent;
    private readonly int _triggerThreshold;
    private readonly string _prompt;

    public CustomSummarizingReducer(
        IChatClient summarizer,
        int keepRecent = 4,
        int triggerThreshold = 2,
        string? prompt = null)
    {
        _summarizer = summarizer;
        _keepRecent = keepRecent;
        _triggerThreshold = triggerThreshold;
        _prompt = prompt ?? """
            请将以下对话历史压缩为一段简洁摘要（不超过3句话）：
            - 提取用户的核心需求、关键信息、已做出的承诺
            - 忽略寒暄和重复内容
            - 如果之前已有摘要，请将其内容合并进来
            """;
    }

    public async Task<IEnumerable<ChatMessage>> ReduceAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();

        // 分离 System 消息（始终保留）
        var systemMessages = list.Where(m => m.Role == ChatRole.System).ToList();
        var nonSystem = list.Where(m => m.Role != ChatRole.System).ToList();

        // 如果没超阈值，原样返回
        if (nonSystem.Count <= _keepRecent + _triggerThreshold)
            return list;

        // 找到已有的摘要消息，合并进待压缩内容
        var existingSummary = nonSystem
            .Where(m => m.Role == ChatRole.User &&
                        m.Contents.OfType<TextContent>().Any(c => c.Text.StartsWith("[Summary]")))
            .LastOrDefault();

        var toSummarize = new List<string>();
        if (existingSummary != null)
        {
            var text = existingSummary.Contents.OfType<TextContent>().First().Text;
            toSummarize.Add(text);
        }

        // 取需要压缩的旧消息（去掉最近 keepRecent 条）
        var oldMessages = nonSystem.Take(nonSystem.Count - _keepRecent).ToList();
        foreach (var msg in oldMessages)
        {
            if (msg.Role == ChatRole.User || msg.Role == ChatRole.Assistant)
            {
                var text = string.Join("\n", msg.Contents.OfType<TextContent>().Select(c => c.Text));
                if (!string.IsNullOrWhiteSpace(text))
                    toSummarize.Add($"[{msg.Role}]: {text}");
            }
        }

        // 调用摘要模型
        var summaryText = await _summarizer.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, _prompt + "\n\n" + string.Join("\n", toSummarize)) },
            cancellationToken: cancellationToken);

        var summaryMsg = new ChatMessage(ChatRole.User, $"[Summary] {summaryText.Text}");

        // 返回：System + 摘要 + 最近 keepRecent 条
        var recent = nonSystem.Skip(nonSystem.Count - _keepRecent).ToList();
        var result = new List<ChatMessage>();
        result.AddRange(systemMessages);
        result.Add(summaryMsg);
        result.AddRange(recent);
        Console.WriteLine("压缩完成");
        return result;
    }
}