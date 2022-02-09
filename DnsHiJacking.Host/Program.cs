using DnsHiJacking.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = null;
    kestrel.Limits.MinResponseDataRate = null;
    kestrel.Limits.MinRequestBodyDataRate = null;
    // kestrel.ListenHttpsReverseProxy(); 
    kestrel.ListenLocalhost(80);

    kestrel.ListenLocalhost(5000);
    kestrel.ListenLocalhost(443, listen =>
    {
        listen.UseHttps(https =>
        {
            string CACERT_PATH = "cacert";
            /// <summary>
            /// 获取证书文件路径
            /// </summary>
            string CaCerFilePath = OperatingSystem.IsLinux() ? $"{CACERT_PATH}/fastgithub.crt" : $"{CACERT_PATH}/fastgithub.cer";

            /// <summary>
            /// 获取私钥文件路径
            /// </summary>
            string CaKeyFilePath = $"{CACERT_PATH}/fastgithub.key";
            var validFrom = DateTime.Today.AddDays(-1);
            var validTo = DateTime.Today.AddYears(1);

            https.ServerCertificateSelector = (ctx, domain) => CertGenerator.GenerateByCa(GetDomains(domain).Distinct(), 2048, validFrom, validTo, CaCerFilePath, CaKeyFilePath);
        });
    });
    kestrel.ConfigureHttpsDefaults(adapterOptions =>
    {
        adapterOptions.ClientCertificateMode = ClientCertificateMode.DelayCertificate;
    });
});
/// <summary>
/// 获取域名
/// </summary>
/// <param name="domain"></param>
/// <returns></returns>
static IEnumerable<string> GetDomains(string? domain)
{
    if (string.IsNullOrEmpty(domain) == false)
    {
        yield return domain;
        yield break;
    }

    yield return Environment.MachineName;
    yield return IPAddress.Loopback.ToString();
    yield return IPAddress.IPv6Loopback.ToString();
}

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddHttpForwarder();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();
app.MapWhen(context => context.Connection.LocalPort != 5000, HandleMapTest1);
void HandleMapTest1(IApplicationBuilder app)
{
    app.Run(async context =>
    {
        var scheme = context.Request.Scheme;
        var host = context.Request.Host;
        var destinationPrefix = $"{scheme}://{host}/"; 
        IHttpForwarder httpForwarder = context.RequestServices.GetRequiredService<IHttpForwarder>();
        var httpClient = new HttpMessageInvoker(new SocketsHttpHandler()

        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false
        });

        var error = await httpForwarder.SendAsync(context, destinationPrefix, httpClient);

        await HandleErrorAsync(context, error);

    });

}

/// <summary>
/// 处理错误信息
/// </summary>
/// <param name="context"></param>
/// <param name="error"></param>
/// <returns></returns>
static async Task HandleErrorAsync(HttpContext context, ForwarderError error)
{
    if (error == ForwarderError.None || context.Response.HasStarted)
    {
        return;
    }

    await context.Response.WriteAsJsonAsync(new
    {
        error = error.ToString(),
        message = context.GetForwarderErrorFeature()?.Exception?.Message
    });
}
app.MapRazorPages();
//app.Map("/", (a) =>
//{
//    System.Console.WriteLine("收到");
//    return Task.FromResult(new { code = 1, data = 2 });
//});

app.Run();


sealed class LifetimeHttpHandler : DelegatingHandler
{
    public LifetimeHttpHandler()
    {

    }
}