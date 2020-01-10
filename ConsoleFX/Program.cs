using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleFX
{
    class Program
    {
        static void Main(string[] args)
        {
            ImageHelper.CompressImage(AppDomain.CurrentDomain.BaseDirectory+ "\\.net core学习路线图.png", AppDomain.CurrentDomain.BaseDirectory + "\\a.png");
        }
    }
}
