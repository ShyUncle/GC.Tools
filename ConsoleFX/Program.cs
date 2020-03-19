using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO;
namespace ConsoleFX
{
    class Program
    {
        static void Main(string[] args)
        {
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create("http://localhost:9999/store/PayQrCode?orderId=24&payMethod=2");
            request.Method = "Get";
            using (var stream = request.GetResponse().GetResponseStream())
            {
                using (FileStream file = new FileStream("abc.png", FileMode.Create))
                {
                    stream.CopyTo(file);
                    file.Flush();
                }
            }

            ImageHelper.CompressImage(AppDomain.CurrentDomain.BaseDirectory + "\\.net core学习路线图.png", AppDomain.CurrentDomain.BaseDirectory + "\\a.png");
        }
    }
}
