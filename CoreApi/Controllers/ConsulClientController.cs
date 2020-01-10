using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Consul;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CoreApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConsulClientController : ControllerBase
    {
        [HttpGet]
        public async Task<string> Get()
        {
            using (var client = new ConsulClient((config) =>
            {
                config.Token = "123456";
            }))
            {
                var putPair = new KVPair("hello")
                {
                    Value = Encoding.UTF8.GetBytes("Hello Consul")
                };

                var putAttempt = await client.KV.Put(putPair);

                if (putAttempt.Response)
                {
                    var getPair = await client.KV.Get("hello");
                    return Encoding.UTF8.GetString(getPair.Response.Value, 0,
                        getPair.Response.Value.Length);
                }

                return "";
            }
        }

        [HttpGet]
        [Route("service")]
        public async Task<long> Get1(string str)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            List<Task> list = new List<Task>();
            //本机测试qps 1,364   1000请求 733毫秒
            using (var client = new ConsulClient((config) =>
            {
                config.Token = "123456";
            }))
            {
                for (var i = 0; i < 1000; i++)
                {
                    list.Add(client.Catalog.Service("userService"));
                }

                Task.WaitAll(list.ToArray());
            }
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        [HttpGet]
        [Route("service1")]
        public async Task<string> Get2(string str)
        {
            using (var client = new ConsulClient((config) =>
            {
                config.Token = "123456";
            }))
            {
                var service = await client.Catalog.Service("userService");
                var a = service.Response[0];
                var s = await new HttpClient().GetAsync($"http://{a.ServiceAddress}:{a.ServicePort}/WeatherForecast");
                var dd = await s.Content.ReadAsStringAsync();
                return dd;
            }
        }
    }
}