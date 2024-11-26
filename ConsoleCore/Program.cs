using System.Threading.Tasks;
using GC.Tools.gRPCTest;
using Grpc.Net.Client;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using GC.Tools.GRPCServer;
using System.Net.Http;
using ICSharpCode.SharpZipLib.Zip;
using System.Text;
using System.IO;
using System.Linq.Expressions;
using ShardingEFCoreTest;
using System.Data.Common;

namespace ConsoleCore
{
    class Program
    {
        static void Main(string[] args)
        {

            var list = new EnumrableTest<int>(new List<int> { 1, 3, 4, 2, 5, 6 });
            foreach (var item in list)
            {
                Console.WriteLine(item);
            }
            foreach (var item in list)
            {
                Console.WriteLine(item);
            }
            Console.ReadKey();
        }

    }
}
