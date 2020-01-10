using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;

namespace GC.Tools.HangFireTest
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // Add Hangfire services.
            services.AddHangfire(configuration => configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(Configuration.GetConnectionString("HangfireConnection"), new SqlServerStorageOptions
                {
                    CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                    SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                    QueuePollInterval = TimeSpan.Zero,
                    UseRecommendedIsolationLevel = true,
                    UsePageLocksOnDequeue = true,
                    DisableGlobalLocks = true
                }));

            // Add the processing server as IHostedService
            services.AddHangfireServer(configuration =>
            {
                configuration.ServerName = "定时1";
                configuration.SchedulePollingInterval = TimeSpan.FromMilliseconds(500);
            }
            );

        }

        public void ss()
        {
            Console.WriteLine("定时执行5" + DateTime.Now);
        }
        public void ss2()
        {
            Console.WriteLine("定时执行2" + DateTime.Now);
        }
        public void ABDSD()
        {
            Console.WriteLine("新型智能");
        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IBackgroundJobClient backgroundJobs)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseHangfireDashboard(options: new DashboardOptions()
            {
                DashboardTitle = "hangfireTest"
            });
            backgroundJobs.Enqueue(() => Console.WriteLine("Hello world from Hangfire!"));
            backgroundJobs.Schedule(() => ss(), TimeSpan.FromSeconds(5));

            RecurringJob.AddOrUpdate("客户端", () => Console.WriteLine("定时执行5" + DateTime.Now), "*/5 * * * * *");
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.Map("/", async context =>
                {
                    backgroundJobs.Enqueue(() => Console.WriteLine("新型智能"));
                    await context.Response.WriteAsync("Hello World!");
                });
            });
        }
    }
}
