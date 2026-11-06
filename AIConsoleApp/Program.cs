using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using System.ClientModel;
using System.ComponentModel;
namespace AIConsoleApp
{
    internal class Program
    {
        static void ColorConsoleWriteLine(string str, ConsoleColor consoleColor)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = consoleColor; // 或其他颜色
            Console.WriteLine(str);
            Console.ForegroundColor = originalColor;
        }
        private const string endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/";
        private const string modelName = "qwen3.8-2.4t-a95b";
        static async Task Main(string[] args)
        {
            var apiKey = Environment.GetEnvironmentVariable("api-key");
            if (string.IsNullOrEmpty(apiKey))
            {
                ColorConsoleWriteLine("key不存在", ConsoleColor.Red);
            }
            else
            {
                ColorConsoleWriteLine(apiKey, ConsoleColor.Green);
            }
            ColorConsoleWriteLine($"course search mode = {CourseSearchTools.ModeName}", ConsoleColor.DarkYellow);

            // 连接 MCP Server
            //var transport = new StdioClientTransport(new StdioClientTransportOptions
            //{
            //    Name = "Product MCP Server",
            //    Command = "dotnet",
            //    Arguments = ["run", "--project", "D:\\daydayup\\GC.Tools\\ProductMcpServer"],
            //});
            //await using var mcpClient = await McpClient.CreateAsync(transport);
            //var mcpTools = await mcpClient.ListToolsAsync();


            IList<McpClientTool> httpTools = null;
            try
            {
                var httpTransport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri("https://localhost:7259/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                });
                await using var httpClient = await McpClient.CreateAsync(httpTransport);
                httpTools = await httpClient.ListToolsAsync();
            }
            catch (Exception ex)
            {
                ColorConsoleWriteLine($"连接MCP Server失败: {ex.Message}", ConsoleColor.Red);
            }
            //tools
            var queryProducts = AIFunctionFactory.Create(
                    async (
                        [Description("商品名称，例如：手机、耳机")] string? name,
                        [Description("商品类别，例如：无线耳机、笔记本电脑")] string? category,
                        [Description("最低价格，单位元")] decimal? minPrice, [Description("最高价格，单位元")] decimal? maxPrice,
                        CancellationToken ct) =>
                    {
                        return await ProductClient.QueryProductsAsync(name, category, minPrice, maxPrice, ct);
                    },
                    name: "query_products",
                    description: "查询【藏宝阁】商品信息。当用户想找藏宝阁的商品、筛选价格、按类别查找商品时调用。返回商品名称、类别和价格。"
             );//帮我找下藏宝阁7000元左右的类型为智能手机的手机
            var searchCourseMaterials = AIFunctionFactory.Create(CourseSearchTools.SearchCourseMaterials);//我想学中级会计的长期股权投资，有相关课程推荐吗
            var options = new ChatOptions
            {
                Tools = [
                   AIFunctionFactory.Create(WeatherTools.GetLocation),
                   AIFunctionFactory.Create(WeatherTools.GetWeather),
                 //  queryProducts,
                   searchCourseMaterials,
               ],

            };
            //加载MCP Server工具和Http工具，其实是同一个工具，只是加载方式不同
            //foreach (var item in mcpTools)
            //{
            //     options.Tools.Add(item);
            //}
            if (httpTools != null)
            {
                foreach (var item in httpTools)
                {
                    options.Tools.Add(item);
                }
            }
            var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) }).GetChatClient(modelName).AsIChatClient();
            var summaryClient = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) }).GetChatClient(modelName).AsIChatClient();


            //自动调用 摘要模型，压缩对话历史，减少token消耗
            client = new ChatClientBuilder(client).UseChatReducer(new CustomSummarizingReducer(summaryClient, keepRecent: 20, triggerThreshold: 20)).UseFunctionInvocation(configure: client =>
            {
                client.FunctionInvoker = async (context, cancellationToken) =>
                {
                    ColorConsoleWriteLine($"[调用前] {context.Function.Name}", ConsoleColor.DarkCyan);
                    ColorConsoleWriteLine($"[参数] {System.Text.Json.JsonSerializer.Serialize(context.Arguments)}", ConsoleColor.DarkCyan);

                    var result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);

                    ColorConsoleWriteLine($"[调用后] {context.Function.Name} -> {result}", ConsoleColor.DarkCyan);

                    return result;
                };
            }).Build();

            var messages = new List<ChatMessage>();
            messages.Add(new ChatMessage(
              ChatRole.System, $@"你是财会答疑老师""小林""，核心职责是解答学员在财会学习、考证、实操中遇到的具体问题。语气专业但亲切，回复简洁清晰，2-3句话为主。

规则：
1. 只回答财会相关问题（如会计分录、税法计算、考证备考、财务报表等），非财会话题不回复，等待用户提出财会问题。
2. 全程保持答疑老师角色，不承认自己是AI。
3. 不用敬语，不自我介绍。
4. 用户质疑身份、投诉、退款等非财会话题时，统一回复""这个情况我帮你记录反馈一下""，然后引导回财会问题。
5. 解答问题时先确认关键信息再作答，不硬答。追问可用""你想问的是哪个税种？""""具体是哪笔业务的分录？""
6. 公式、税率、政策等数据必须基于检索到的真实信息，严禁编造。涉及需要确认的信息时，可用""我想想""""稍等""自然过渡。
7. 不确定的内容不要猜测，可以等待用户提供更多信息。
8. 回复风格要求：
   - 开头直接给结论，不绕弯子
   - 用具体数字举例增强可信度
   - 不用markdown加粗等格式符号，用""第一""""第二""或直接分句
   - 结论性表述适当留余地，加""一般""""通常""等缓冲词
9. 对话结束判断：当学员回复""好的""""谢谢""""明白了""""收到""等简短确认或无关紧要内容时，不再继续追问或延伸，直接结束本轮对话。
10. 主动反问限制：给出解答后不主动延伸反问学员，直接结束回答。仅在信息严重不足、无法作答时才追问关键信息
11. 回答优先：无论是否推荐课程，都先把学员的问题回答清楚、完整；推荐只是附加内容，不能为了推荐课程牺牲答案质量。
12. 课程推荐判断：回答完问题后，再判断学员的问题或所问的知识点是否与财会课程资料库中的课程相关。只有判断相关时才调用 SearchCourseMaterials 检索课程资料，不要每轮都调用，也不要为了推荐而调用。
13. 有命中且相关时：在回答末尾另起一小段""推荐学习""，写明课程名称、一句为什么匹配（对应哪个章节或知识点）、检索到的视频地址（若有）、建议的学习顺序。只推荐真正检索到且相关的课程，严禁编造课程名称、章节名称或视频链接。
14. 未命中或明显不相关时：不提任何课程推荐，正常结束回答，不要硬凑课程。
15. 推荐部分保持简短：最多 3 门课程、不超过 4 行，语气像老师引导而不是广告，不影响正文可读性。"
             ));
            while (true)
            {
                Console.Write("请输入：");
                var userInput = Console.ReadLine();
                messages.Add(new ChatMessage(ChatRole.User, userInput));
              //  messages.Add(new ChatMessage(ChatRole.User,new List<AIContent> { new UriContent(new Uri("https://img.kjjl100.com/Question/2023/6/21/20236211433326792827513.jpg")) }));
                var res = await client.GetResponseAsync(messages, options);
                messages.AddMessages(res);
                foreach (var content in res.Messages.FirstOrDefault().Contents)
                {
                    if (content is TextReasoningContent reasoning)
                    {
                        ColorConsoleWriteLine("🧠模型思考过程：", ConsoleColor.Cyan);
                        ColorConsoleWriteLine(reasoning.Text, ConsoleColor.DarkCyan);
                    }
                }
                ColorConsoleWriteLine($"输入{res.Usage.InputTokenCount},输出Token:{res.Usage.OutputTokenCount}, 总token数：{res.Usage.TotalTokenCount}", ConsoleColor.Yellow);
                Console.WriteLine("结果：" + res.Text);
            }
        }

    }
}
