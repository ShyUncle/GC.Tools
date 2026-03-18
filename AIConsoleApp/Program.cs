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
            var endpoint = "https://api.deepseek.com/v1";
            var modelName = "deepseek-chat";

            var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) }).GetChatClient(modelName).AsIChatClient();
            var res = await client.GetResponseAsync("你好");
            Console.WriteLine(res.Text);
            Console.ReadLine();
        }
    }
}
