using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
namespace AIConsoleApp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var apiKey = Environment.GetEnvironmentVariable("api-key");
            var endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/";
            var modelName = "tongyi-xiaomi-analysis-pro";

            var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) }).GetChatClient(modelName).AsIChatClient();
            var messages = new List<ChatMessage>();
            messages.Add(new ChatMessage(
              ChatRole.System,
                "你是一个万能助手，可以回答任何问题"
           ));
            while (true)
            {
                Console.Write("请输入：");
                var userInput = Console.ReadLine();
                messages.Add(new ChatMessage(ChatRole.User, userInput));
                messages = await SMessageAsync(messages, client);
                var res = await client.GetResponseAsync(messages);
                messages.AddMessages(res);
                Console.WriteLine("结果：" + res.Text);
            }
        }

        static async Task<List<ChatMessage>> SMessageAsync(List<ChatMessage> messages, IChatClient client)
        {
            if (messages.Count > 10)
            {
                var mes = new List<ChatMessage>();
                foreach (var item in messages.Skip(1).Take(5))
                {
                    mes.Add(item);
                }
                mes.Add(new ChatMessage(ChatRole.User, "请简要概括以上对话的内容"));
                var res = await client.GetResponseAsync(mes);
                messages.RemoveRange(1, messages.Count - 5);
                messages.Insert(1, new ChatMessage(ChatRole.User,"对话简要："+ res.Text));
            }
            return messages;
        }
    }
}
