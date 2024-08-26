using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SignalRSample.Hubs;
using StackExchange.Redis;
using System.Text;
using static SignalRSample.LanguageFilterAttribute;

namespace SignalRSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();

            builder.Services.AddSingleton<LanguageFilter>();
            builder.Services.AddAuthentication(options =>
            {
                // Identity made Cookie authentication the default.
                // However, we want JWT Bearer Auth to be the default.
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                // Configure the Authority to the expected value for
                // the authentication provider. This ensures the token
                // is appropriately validated.
                //  options.Authority = "Authority URL"; // TODO: Update URL
                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        LifetimeValidator = (before, expires, token, param) =>
                        {
                            return expires > DateTime.UtcNow;
                        },
                        ValidateAudience = false,
                        ValidateIssuer = false,
                        ValidateActor = false,
                        ValidateLifetime = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes("sdf1d643!32_32aksdf1d643!32_32aksdf1d643!32_32ak"))
                    };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];

                        // If the request is for our hub...
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) &&
                            (path.StartsWithSegments("/chatHub")))
                        {
                            // Read the token out of the query string
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });
        //    builder.Services.AddSingleton<IMemoryCache>();
            builder.Services.AddSignalR(hubOptions =>
            {
                hubOptions.AddFilter<LanguageFilter>();
            }).AddHubOptions<ChatHub>(options =>
            {
                options.AddFilter<CustomFilter>();
            });

            //builder.Services.AddRateLimiter(options =>
            //{
            //    //1、固定窗口限流策略 
            //    options.AddFixedWindowLimiter(policyName: "fixed", fixedOptions =>
            //    {
            //        fixedOptions.PermitLimit = 5;// 
            //        fixedOptions.Window = TimeSpan.FromSeconds(60); //窗口大小。即窗口时间长度 
            //        fixedOptions.QueueLimit = 3;//窗口阈值。即每个窗口时间范围内，最多允许的请求个数。该值必须>0。当窗口请求达到最大值，后续请求会进入排队。该值用于设置对垒大小（即允许几个请求在排队队列等待）
            //        fixedOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;//排队请求的处理顺序。这里设置为有限处理先来的请求
            //        fixedOptions.AutoReplenishment = true;//开启新窗口时是否自动重置请求限制，默认true。如果是false，则需要手动调佣 FixedWindowRateLimiter.TryReplenish来重置
            //    });
            //});
            builder.Services.AddControllers();
            builder.Services.AddSignalR().AddStackExchangeRedis(builder.Configuration.GetValue<string>("Redis:Configuration")!, options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("MyApp");
            });
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            // app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapRazorPages();
            app.MapControllers();    //使用限流器

            app.MapHub<ChatHub>("/chatHub");

            app.Run();
        }
    }
}
