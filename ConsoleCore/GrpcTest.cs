using GC.Tools.gRPCTest;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleCore
{
   public class GrpcTest
    {
        /// <summary>
        /// 异步并发请求
        /// </summary>
     public    static void GRPCTestAsync()
        {
            var channel = GrpcChannel.ForAddress("https://localhost:5001");
            int len = 10000;
            while (true)
            {
                //grpc请求10000次时间839ms,每个请求0.0839ms
                // http请求10000次时间1398ms,每个请求0.1398ms
                Stopwatch sw = new Stopwatch();
                sw.Start();

                List<Task> lsit = new List<Task>();
                for (var i = 0; i < len; i++)
                {
                    var client = new Greeter.GreeterClient(channel);
                    lsit.Add(client.SayHelloAsync(new HelloRequest { Name = "Client" }).ResponseAsync);
                }
                Task.WaitAll(lsit.ToArray());
                sw.Stop();
                Console.WriteLine($"grpc请求{len}次时间" + sw.ElapsedMilliseconds + "ms,每个请求{0}ms", sw.ElapsedMilliseconds / (decimal)len);
                lsit.Clear();
                sw.Start();
                var httpClient = new HttpClient()
                {
                    DefaultRequestVersion = new Version(2, 0)
                };

                for (var i = 0; i < len; i++)
                {
                    lsit.Add(httpClient.GetStringAsync("https://localhost:5001"));
                }
                Task.WaitAll(lsit.ToArray());
                sw.Stop();
                Console.WriteLine($"http请求{len}次时间" + sw.ElapsedMilliseconds + "ms,每个请求{0}ms", sw.ElapsedMilliseconds / (decimal)len);
                Console.ReadLine();
            }
        }

        /// <summary>
        /// 同步请求
        /// </summary>
        public static void GRPCTest()
        {
            var channel = GrpcChannel.ForAddress("https://localhost:5001");
            int len = 10000;
            while (true)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                //  grpc请求10000次时间2325ms,每个请求0.2325ms
                //    http请求10000次时间4320ms, 每个请求0.432ms
                for (var i = 0; i < len; i++)
                {
                    try
                    {
                        var client = new Greeter.GreeterClient(channel);

                        client.SayHello(new HelloRequest { Name = "Client" });
                    }
                    catch (Grpc.Core.RpcException ex)
                    {

                    }
                }
                sw.Stop();
                Console.WriteLine($"grpc请求{len}次时间" + sw.ElapsedMilliseconds + "ms,每个请求{0}ms", sw.ElapsedMilliseconds / (decimal)len);
                sw.Start();
                var httpClient = new HttpClient()
                {
                    DefaultRequestVersion = new Version(2, 0)
                };
                for (var i = 0; i < len; i++)
                {
                    var aaa = httpClient.GetStringAsync("https://localhost:5001").Result;
                }
                sw.Stop();
                Console.WriteLine($"http请求{len}次时间" + sw.ElapsedMilliseconds + "ms,每个请求{0}ms", sw.ElapsedMilliseconds / (decimal)len);
                Console.ReadLine();
            }
        }
    }
}
