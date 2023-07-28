using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;

namespace WechatPayTest.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private WechatTenpayClientOptions _tenpayClientOptions;

        private static readonly string[] Summaries = new[]
        {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(IOptions<WechatTenpayClientOptions> tenpayClientOptions, ILogger<WeatherForecastController> logger)
        {
            this._tenpayClientOptions = tenpayClientOptions.Value;
            _logger = logger;
        }

        [HttpGet(Name = "GetWeatherForecast")]
        public async Task<IEnumerable<WeatherForecast>> Get()
        {
            var client = new WechatTenpayClient(_tenpayClientOptions);
            var result = await client.ExecuteCreateEcommerceApplymentAsync(new SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.CreateEcommerceApplymentRequest() {
              
            }); 
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }
    }
}