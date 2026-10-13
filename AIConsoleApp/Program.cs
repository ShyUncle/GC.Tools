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
        private const string modelName = "qwen3.7-flash";
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
            var options = new ChatOptions
            {
                Tools = [
                   AIFunctionFactory.Create(WeatherTools.GetLocation),
                   AIFunctionFactory.Create(WeatherTools.GetWeather),
                   queryProducts,
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


            //自动调用
            client = new ChatClientBuilder(client).UseChatReducer(new CustomSummarizingReducer(summaryClient, keepRecent: 20, triggerThreshold: 20)).UseFunctionInvocation(configure: client =>
            {
                client.FunctionInvoker = async (context, cancellationToken) =>
                {
                    Console.WriteLine($"[调用前] {context.Function.Name}");
                    Console.WriteLine($"[参数] {System.Text.Json.JsonSerializer.Serialize(context.Arguments)}");

                    var result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);

                    Console.WriteLine($"[调用后] {context.Function.Name} -> {result}");

                    return result;
                };
            }).Build();

            var messages = new List<ChatMessage>();
            messages.Add(new ChatMessage(
              ChatRole.System, "你是一个万能助手，可以回答任何问题"
             ));
            while (true)
            {
                Console.Write("请输入：");
                var userInput = Console.ReadLine();
                messages.Add(new ChatMessage(ChatRole.User, userInput));
                var res = await client.GetResponseAsync(messages, options);
                messages.AddMessages(res);
                foreach (var content in res.Messages.FirstOrDefault().Contents)
                {
                    if (content is TextReasoningContent reasoning)
                    {
                        ColorConsoleWriteLine("🧠模型思考过程：", ConsoleColor.Cyan);
                        ColorConsoleWriteLine(reasoning.Text, ConsoleColor.Cyan);
                    }
                }
                ColorConsoleWriteLine($"输入{res.Usage.InputTokenCount},输出Token:{res.Usage.OutputTokenCount}, 总token数：{res.Usage.TotalTokenCount}", ConsoleColor.Yellow);
                Console.WriteLine("结果：" + res.Text);
            }
        }

    }
}
