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
        public string Get()
        {
            var c = new AsyncTest();
            c.Current = new testDate();
            System.Threading.Thread.Sleep(20000);
            c = new AsyncTest();
            return c.Current.date.ToString();
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