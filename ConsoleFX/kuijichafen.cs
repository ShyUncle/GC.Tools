using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace ConsoleFX
{
    public class kuijichafen
    {
        public static List<string> HostList = new List<string> { "kzp.mof.gov.cn", "221.181.73.5:81/", "60.208.116.171/", "103.59.150.151:81/" };
        public string Host { get; set; }
        public string GetHost()
        {
            if (string.IsNullOrEmpty(Host))
            {
                var random = new Random(DateTime.Now.Millisecond);
                var idnex = random.Next(0, HostList.Count);
                Host = HostList[idnex];
            }

            return Host;
        }
        public void MakeCommonRequest(HttpWebRequest request)
        {
            request.Timeout = 10000;
            request.KeepAlive = true;
            request.Referer = $"http://{GetHost()}/cjcx/1_c4036a022596222.jsp";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/85.0.4183.121 Safari/537.36";
        }
        public void JieXi(string html)
        {

            //从url中加载
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html); //加载html
            //获得title标签节点，其子标签下的所有节点也在其中
            HtmlNode headNode = doc.DocumentNode.SelectSingleNode("//div[@class='cjcx_z']");
            //获得title标签中的内容
            string title = headNode.InnerText;
            title = new Regex(@"\s+(?=\S)").Replace(title, ";");
            var arr = title.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in arr)
            {
                var b = item.Split('：');
                Console.WriteLine(b[0].Trim() + ":" + b[1].Trim());
            }
            HtmlNode tableNode = doc.DocumentNode.SelectSingleNode("//table[@class='cjcx_jg']");
            var tds = tableNode.SelectNodes("child::tr[3]/td");
            Console.WriteLine(tds[0].InnerText + ":" + tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""));
            tds = tableNode.SelectNodes("child::tr[4]/td");
            Console.WriteLine(tds[0].InnerText + ":" + tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""));
        }
        public void Get(string idcard, string name, string provinceId)
        {
            Console.WriteLine(GetHost());
            var cc = GetCookie();
            var code = GetCode(cc);
            if (string.IsNullOrEmpty(code) || code.Length != 4)
            {
                code = GetCode(cc);
            }
            if (string.IsNullOrEmpty(code) || code.Length != 4)
            {
                Console.WriteLine("验证码识别错误");
                return;
            }
            var uri = new Uri($"http://{GetHost()}/cjcx/2_c4036a022596222.jsp");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.ContentType = "application/x-www-form-urlencoded";
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.SetCookies(uri, cc);
            request.Method = "post";
            MakeCommonRequest(request);
            string str = $"province={provinceId}&sfzh={idcard}&xm={ HttpUtility.UrlEncode(name)}&atch={code}";
            byte[] buffer = Encoding.Default.GetBytes(str);
            request.ContentLength = buffer.Length;
            request.GetRequestStream().Write(buffer, 0, buffer.Length);
            var response = (HttpWebResponse)request.GetResponse();
            StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
            string result = reader.ReadToEnd();
            JieXi(result);
        }
        public string GetCookie()
        {
            var uri = new Uri($"http://{GetHost()}/cjcx/1_c4036a022596222.jsp");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = true;
            request.Method = "get";
            MakeCommonRequest(request);
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            response.GetResponseStream();
            if (response.Headers.Get("Set-Cookie") != "")
            {
                Console.WriteLine("第一次" + response.Headers.Get("Set-Cookie"));
                return response.Headers.Get("Set-Cookie");
            }
            return "";
        }
        public string GetCode(string cc)
        {
            var stamp = (DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
            string host = $"http://{GetHost()}/cjcx/img.jsp?timestamp=" + stamp;

            Encoding encoding = Encoding.Default;
            var request = (HttpWebRequest)WebRequest.Create(host);
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Method = "get";
            request.Headers.Set("Cookie", cc);
            MakeCommonRequest(request);
            var response = (HttpWebResponse)request.GetResponse();

            var stream = response.GetResponseStream();

            byte[] arr = ReadFully(stream);
            stream.Dispose();
            var code1 = RecognitionImage(arr);

            Console.WriteLine("验证码" + code1);
            return code1;
        }
        /// <summary>
        /// 网络流转换为Byte数组
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static byte[] ReadFully(Stream stream)
        {
            byte[] buffer = new byte[128];
            using (MemoryStream ms = new MemoryStream())
            {
                while (true)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        return ms.ToArray();
                    ms.Write(buffer, 0, read);
                }
            }
        }
        static string RecognitionImage(byte[] image)
        {
            // 设置APPID/AK/SK
            var APP_ID = "21512957";
            var API_KEY = "OFez8onF6CqQ8khtNnecrWGD";
            var SECRET_KEY = "2aAt05myciGn6DvGPGLAL8LQ5v4kMm56";
            var client = new Baidu.Aip.Ocr.Ocr(API_KEY, SECRET_KEY);
            client.Timeout = 60000;  // 修改超时时间

            //   var image = File.ReadAllBytes(@"C:\Users\admin\Desktop\微信图片_20200930104921.png");
            // 调用通用文字识别, 图片参数为本地图片，可能会抛出网络等异常，请使用try/catch捕获
            var result1 = client.GeneralBasic(image);

            var word = result1.GetValue("words_result");
            var a = JsonConvert.DeserializeObject<List<Words>>(word.ToString());
            var b = a.FirstOrDefault()?.words;

            return b;
        }

        public class Words
        {
            public string words { get; set; }
        }
    }
}
