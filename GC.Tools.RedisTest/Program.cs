using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GC.Tools.RedisTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Parallel.For(1, 1000, (i) =>
                {
                    var redis = new RedisHelper();
                    redis.StringSet("name", i.ToString());
                    Console.WriteLine(redis.StringGet("name"));
                });
            Console.WriteLine("完成");
            Console.ReadLine();
        }
    }
}
