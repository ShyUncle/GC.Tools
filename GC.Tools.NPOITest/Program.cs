using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GC.Tools.NPOITest
{
    class Program
    {

        static void Main(string[] args)
        {
            var path = Console.ReadLine();
            path = string.IsNullOrEmpty(path) ? @"C:\Users\admin\Desktop\tes.docx" : path;
            while (true)
            {
                new NpoiWordHelper().ReadWorld(path);
                Console.ReadLine();
            }
        }

    }

}
