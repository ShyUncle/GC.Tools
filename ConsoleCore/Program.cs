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

namespace ConsoleCore
{
    class Program
    {
        static void Main(string[] args)
        {
            //  ImageHelper.CompressImage(AppDomain.CurrentDomain.BaseDirectory + "\\.net core学习路线图.png", AppDomain.CurrentDomain.BaseDirectory + "\\a.png");
            // GrpcTest.GRPCTest();
            var gbk = Encoding.GetEncoding("GBK");
           var codec=StringCodec.FromCodePage( gbk.CodePage);
            string outputFilePath = "";
            List<string> files = new List<string>();
            using (var zipOutputStream = new ZipOutputStream(File.Create(outputFilePath),codec))
            {
                zipOutputStream.SetLevel(9); // 设置压缩级别
                foreach (var file in files)
                {
                    var entry = new ZipEntry(Path.GetFileName(file)) { DateTime = DateTime.Now };
                    zipOutputStream.PutNextEntry(entry);
                    using (var fileStream = File.OpenRead(file))
                    {
                        byte[] buffer = new byte[4096];
                        int read;
                        while ((read = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            zipOutputStream.Write(buffer, 0, read);
                        }
                    }
                }
                zipOutputStream.Close(); // 完成压缩操作后关闭流
            }

            using (var zipStream = new ZipInputStream(File.OpenRead(outputFilePath),codec)) {
            
            }
            Console.ReadKey();
        }


    }
}
