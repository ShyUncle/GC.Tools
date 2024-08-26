using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using SignalRSample.Hubs;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SignalRSample.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        private readonly IHubContext<ChatHub, IChatClient> _hubContext;

        public ValuesController(IHubContext<ChatHub, IChatClient> hubContext)
        {
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task SayHello()
        {
            await _hubContext.Clients.All.ReceiveMessage("", "hello");
        }

        [HttpGet]
        [Route("single")]
        public async Task SinggleSayHello(string user)
        {
            await _hubContext.Clients.All.ReceiveMessage(user, "hello1");
        }

        [HttpPost]
        [Route("login")]
        public async Task<string> LoginAsync(string user)
        {
            var claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.NameIdentifier, user));
            claims.Add(new Claim(ClaimTypes.Name, user));

            string jwtToken = BuildToken(claims);
            return jwtToken;
        }

        /// <summary>
        /// 生成token的方法
        /// </summary>
        /// <param name="claims"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        private static string BuildToken(IEnumerable<Claim> claims)
        {
            //token到期时间
            DateTime expires = DateTime.Now.AddSeconds(100000);
            //取出配置文件的key
            byte[] keyBytes = Encoding.UTF8.GetBytes("sdf1d643!32_32aksdf1d643!32_32aksdf1d643!32_32ak");
            //对称安全密钥
            var secKey = new SymmetricSecurityKey(keyBytes);
            //加密证书
            var credentials = new SigningCredentials(secKey, SecurityAlgorithms.HmacSha256Signature);
            //jwt安全token
            var tokenDescriptor = new JwtSecurityToken(expires: expires, signingCredentials: credentials, claims: claims);

            return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        }
    }
}
