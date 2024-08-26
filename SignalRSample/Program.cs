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
            //    //1���̶������������� 
            //    options.AddFixedWindowLimiter(policyName: "fixed", fixedOptions =>
            //    {
            //        fixedOptions.PermitLimit = 5;// 
            //        fixedOptions.Window = TimeSpan.FromSeconds(60); //���ڴ�С��������ʱ�䳤�� 
            //        fixedOptions.QueueLimit = 3;//������ֵ����ÿ������ʱ�䷶Χ�ڣ��������������������ֵ����>0������������ﵽ���ֵ���������������Ŷӡ���ֵ�������ö��ݴ�С�����������������ŶӶ��еȴ���
            //        fixedOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;//�Ŷ�����Ĵ���˳����������Ϊ���޴�������������
            //        fixedOptions.AutoReplenishment = true;//�����´���ʱ�Ƿ��Զ������������ƣ�Ĭ��true�������false������Ҫ�ֶ���Ӷ FixedWindowRateLimiter.TryReplenish������
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
            app.MapControllers();    //ʹ��������

            app.MapHub<ChatHub>("/chatHub");

            app.Run();
        }
    }
}
