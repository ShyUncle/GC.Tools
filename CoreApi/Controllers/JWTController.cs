using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CoreApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JWTController : ControllerBase
    {
        [HttpGet]
        public async Task<string> Get()
        {
            var lo = new System.Threading.ThreadLocal<int>();
            lo.Value = 1;
            var c = new AsyncTest();
            c.Current = new testDate();
            var s = c.Current.date.ToString();

            var da = Task.Factory.StartNew(() =>
          {
              System.Threading.Thread.Sleep(10000);
              var aad = new AsyncTest();
              return aad.Current.date.ToString();
          });
            c = new AsyncTest();
            return s + ":" ;
        }
    }
    [Route("api/[controller]")]
    [ApiController]
    public class JWTtestController : ControllerBase
    {
        [HttpGet]
        public string Get()
        {
            var c = new AsyncTest();
            c.Current = new testDate();
            return c.Current.date.ToString();
        }
    }
    [Route("api/[controller]")]
    [ApiController]
    public class JWT1Controller : ControllerBase
    {
        [HttpGet]
        public string Get()
        {
            var c = new AsyncTest();

            return c.Current.date.ToString();
        }
    }
}