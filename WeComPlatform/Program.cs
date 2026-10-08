
using SKIT.FlurlHttpClient.Wechat.Work;

namespace WeComPlatform
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            var options = new WechatWorkClientOptions()
            {
                CorpId = "企业微信 CorpId",
                AgentId = 1,
                AgentSecret = "企业微信应用的 Secret"
            };
            var client = WechatWorkClientBuilder.Create(options).Build();
            builder.Services.AddSingleton<WechatWorkClient>(client);
            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
