using Microsoft.AspNetCore.Mvc;
using SKIT.FlurlHttpClient.Wechat.Work;
 
namespace WeComPlatform.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private WechatWorkClient _workClient;

        public WeatherForecastController(WechatWorkClient workClient)
        {
            _workClient = workClient;
        }

        private static readonly string[] Summaries =
        [
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        ];

        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }

        [HttpGet(Name = "work")]
        public void Get(string model)
        {
            _workClient.ExecuteCgibinChatDataKeywordGetRuleListAsync(new SKIT.FlurlHttpClient.Wechat.Work.Models.CgibinChatDataKeywordGetRuleListRequest { 
              
            });
        }
    }
}
