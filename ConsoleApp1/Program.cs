using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            while (true)
            {
                Console.Read();
                Console.WriteLine("sdf");
            }
        }

        public static void Test()
        {
            var builder = new HostBuilder().ConfigureServices((hostcontext, services) =>
            {
                services.AddTransient<FinishJob>().AddScoped<DependencyA>();
            }).UseConsoleLifetime();
            var host = builder.Build();
            using (var serviceScope = host.Services.CreateScope())
            {
                var serviceProvide = serviceScope.ServiceProvider;
                var task = Task.Factory.StartNew(() =>
                 {
                     var a = serviceProvide.GetService<FinishJob>();
                     var b = serviceProvide.CreateScope();
                     var c = b.ServiceProvider.GetService<FinishJob>();
                     a.Test("a");
                     c.Test("c");
                     b.Dispose();
                     a = serviceProvide.GetService<FinishJob>();
                     a.Test("a");
                 });
                task.Wait();
            }

        }
    }
}
