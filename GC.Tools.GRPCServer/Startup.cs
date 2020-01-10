using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GC.Tools.gRPCTest;
using Grpc.Net.Client;

namespace GC.Tools.GRPCServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc();
        }
        private static GrpcChannel greetChannel = null;
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<ZCService>();

                endpoints.MapGet("/", async context =>
                {
                    if (greetChannel == null)
                    {
                        greetChannel = GrpcChannel.ForAddress("https://localhost:5001");
                    }
                    var cli = new Greeter.GreeterClient(greetChannel);
                    var res = cli.SayHello(new HelloRequest()
                    {
                        Name = "23"
                    });
                    await context.Response.WriteAsync(res.Message);
                });
            });
        }
    }
}
