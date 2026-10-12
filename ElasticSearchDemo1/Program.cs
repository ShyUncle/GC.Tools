
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

namespace ElasticSearchDemo1
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();
            // 配置连接设置
            var settings = new ElasticsearchClientSettings(new Uri("http://localhost:9200")).ServerCertificateValidationCallback((sender, certificate, chain, sslPolicyErrors) => true)
           // .CertificateFingerprint("YOUR_CA_FINGERPRINT") // 生产环境必填，获取方式见下文注释
            .Authentication(new BasicAuthentication("elastic", "")); // 或使用 ApiKey

            // 注册单例客户端
            builder.Services.AddSingleton<ElasticsearchClient>(new ElasticsearchClient(settings));

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
