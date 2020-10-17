using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
        public static List<string> Page = new List<string> { "1_b4036a0225202016", "1_b4036a0225202016" };
        public string Host { get; set; }
        public string GetHost(int type)
        {
            // if (type == 1)
            {
                return HostList[0];
            }
            if (string.IsNullOrEmpty(Host))
            {
                var random = new Random(DateTime.Now.Millisecond);
                var idnex = random.Next(0, HostList.Count);
                Host = HostList[idnex];
            }

            return Host;
        }
        public void MakeCommonRequest(HttpWebRequest request, int type = 0)
        {
            request.Timeout = 10000;
            request.KeepAlive = true;
            request.Referer = $"http://{GetHost(type)}/cjcx/{Page[type]}.jsp";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/85.0.4183.121 Safari/537.36";
        }
        public void JieXi(string html, int type = 0)
        {

            //从url中加载
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html); //加载html
            //获得title标签节点，其子标签下的所有节点也在其中
            HtmlNode headNode = doc.DocumentNode.SelectSingleNode("//div[@class='cjcx_z']");
            if (headNode == null || string.IsNullOrEmpty(headNode.InnerText))
            {
                Console.WriteLine("错误");
                return;
            }
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
            var tds = tableNode.SelectNodes("./tr[@bgcolor='#FFFFFF']");
            foreach (var item in tds)
            {
                var td = item.SelectNodes("./td");
                Console.WriteLine(td[0].InnerText + ":" + td[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""));
            }
            //tds = tableNode.SelectNodes("child::tr[4]/td");
            //Console.WriteLine(tds[0].InnerText + ":" + tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""));
        }
       static  string cookie = "";
        public void Get(string idcard, string name, string provinceId, int type = 0)
        {
            try
            {
                Console.WriteLine(GetHost(type));
                Thread.Sleep(1000);
                string cc = "";
                if (!string.IsNullOrEmpty(cookie))
                {
                    cc = cookie;
                }
                else
                {
                    cc = GetCookie(type);
                    if (!cc.Contains("JSESSIONID"))
                    {
                        cc = cc + ";" + GetCookie(type, cc);
                    }
                    cookie = cc; 
                    Thread.Sleep(1000);
                }
                // 
              
                var code = GetCode(cc, type);
                Console.WriteLine("code" + code);
                for (var i = 0; i < 5; i++)
                {
                    if (string.IsNullOrEmpty(code))
                    {
                        Thread.Sleep(1000);
                        code = GetCode(cc, type);
                    }
                }
                if (string.IsNullOrEmpty(code))
                {
                    Console.WriteLine("验证码识别错误");
                    return;
                }

                Thread.Sleep(500);
                var uri = new Uri($"http://{GetHost(type)}/cjcx/2_{Page[type].Replace("1_", "")}.jsp");
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
                JieXi(result, type);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        public string GetCookie(int type = 0, string cc = "")
        {

            var uri = new Uri($"http://{GetHost(type)}/cjcx/{Page[type]}.jsp");

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = true;
            request.Method = "get";
            if (!string.IsNullOrEmpty(cc))
            {

                request.Headers.Set("Cookie", cc);
            }
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
        public string GetCode(string cc, int type = 0)
        {
            var stamp = (DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
            string host = $"http://{GetHost(type)}/cjcx/imgcal.jsp?timestamp=" + stamp;

            Encoding encoding = Encoding.Default;
            var request = (HttpWebRequest)WebRequest.Create(host);
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Method = "get";
            request.Headers.Set("Cookie", cc);
            MakeCommonRequest(request);
            request.Timeout = 20000;
            var response = (HttpWebResponse)request.GetResponse();

            var stream = response.GetResponseStream();


            var img = Image.FromStream(stream) as Bitmap;
            img.Save("yuan.jpg");
            var newimg = kuijichafen.deleteBorder(kuijichafen.colorClear(kuijichafen.CorlorGray(img)), 1);
            var b = kuijichafen.blackClear(kuijichafen.Threshoding(newimg, 120), 1, 8);
            b.Save("xin.jpg");
            var ste = new MemoryStream();
            b.Save(ste, ImageFormat.Jpeg);
            ste.Seek(0, SeekOrigin.Begin);

            byte[] arr = ReadFully(ste);
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
        public static string RecognitionImage(byte[] image)
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
            try
            {
                var word = result1.GetValue("words_result");
                var a = JsonConvert.DeserializeObject<List<Words>>(word.ToString());
                var b = a.FirstOrDefault()?.words;
                Console.WriteLine("百度验证码结果" + b);
                b = CalcCode(b);
                return b;
            }
            catch (Exception e)
            {
                return "";
            }
        }
        public static string CalcCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return "";
            }
            code = code.Replace("o", "0").Replace("÷", "+").Replace("i", "1");
            var array = code.Split('-', '+', '*');
            //获取数字
            var reg = new Regex("\\d", RegexOptions.IgnoreCase);
            var mts = reg.Matches(code);
            if (mts.Count != 3)
            {
                return "";
            }
            var rego = new Regex("[+*-]", RegexOptions.IgnoreCase);

            var ots = rego.Matches(code);
            string o1 = "";
            string o2 = "";

            if (ots.Count == 0)
            {
                o1 = o2 = "*";
            }
            if (ots.Count == 2)
            {
                o1 = ots[0].Value;
                o2 = ots[1].Value;
            }
            var first = int.Parse(mts[0].Value);

            var sec = int.Parse(mts[1].Value);

            var third = int.Parse(mts[2].Value);
            if (ots.Count == 1)
            {
                if (array[0].Length > 1)
                {
                    o1 = "*";
                    o2 = ots[0].Value;
                }
                else if (array[1].Length > 1)
                {
                    o1 = ots[0].Value;
                    o2 = "*";
                }
            }
            if (string.IsNullOrEmpty(o1) || string.IsNullOrEmpty(o2))
            {
                return "";
            }
            int result = 0;
            if (o1 != "*" && o2 == "*")
            {
                result = Calc(o2, third, sec);
                result = Calc(o1, first, result);
                return result.ToString();
            }
            result = Calc(o1, first, sec);
            result = Calc(o2, result, third);
            return result.ToString();
        }
        static int Calc(string opration, int i, int j)
        {
            int result = 0;
            switch (opration)
            {
                case "-":
                    result = i - j;
                    break;
                case "+":
                    result = i + j;
                    break;
                case "*":
                    result = i * j;
                    break;
            }
            return result;
        }
        public class Words
        {
            public string words { get; set; }
        }

        public static Bitmap CorlorGray(Bitmap bmp)
        {//位图矩形
            System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            //以可读写方式锁定全部位图像素
            System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, bmp.PixelFormat);
            //得到首地址
            IntPtr ptr = bmpData.Scan0;
            //定义被锁定的数组大小，由位图数据与未用空间组成
            int bytes = bmpData.Stride * bmpData.Height;
            byte[] rgbValues = new byte[bytes];
            //复制被锁定的位图像素值到数组中
            System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);
            //灰度化
            double colorTemp = 0;
            for (int i = 0; i < bmpData.Height; i++)
            {
                //只处理每行图像像素数据，舍弃未用空间
                for (int j = 0; j < bmpData.Width * 3; j += 3)
                {
                    colorTemp = rgbValues[i * bmpData.Stride + j + 2] * 0.299 + rgbValues[i * bmpData.Stride + j + 1] * 0.587 + rgbValues[i * bmpData.Stride + j] * 0.114;
                    rgbValues[i * bmpData.Stride + j] = rgbValues[i * bmpData.Stride + j + 1] = rgbValues[i * bmpData.Stride + j + 2] = (byte)colorTemp;
                }
            }
            //把数组复位回位图
            System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, ptr, bytes);
            //解锁位图
            bmp.UnlockBits(bmpData);
            bmp.Save("huidu.jpg");
            return bmp;
        }

        #region 阈值法二值化  

        public static Bitmap Threshoding(Bitmap b, byte threshold)
        {
            int width = b.Width;
            int height = b.Height;
            BitmapData data = b.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            unsafe
            {
                byte* p = (byte*)data.Scan0;
                int offset = data.Stride - width * 4;
                byte R, G, B, gray;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        R = p[2];
                        G = p[1];
                        B = p[0];
                        gray = (byte)((R * 19595 + G * 38469 + B * 7472) >> 16);
                        if (gray >= threshold)
                        {
                            p[0] = p[1] = p[2] = 255;
                        }
                        else
                        {
                            p[0] = p[1] = p[2] = 0;
                        }
                        p += 4;
                    }
                    p += offset;
                }
                b.UnlockBits(data);
                b.Save("erzhi.jpg");
                return b;
            }

        }

        /// <summary>
        /// 进行彩色去噪点
        /// </summary>
        /// <param name="image">原位图</param>
        /// <returns>处理后位图</returns>
        public static Bitmap colorClear(Bitmap image)
        {
            //滤波计算
            //计算该像素点周围8个点及本身的RGB平均值，
            //计算这9个点到平均值的欧式距离。
            //从计算中选取最小的，用其RBG值代替该点的RGB值
            Color currentPixel;
            Bitmap imageResult = new Bitmap(image);
            for (int x = 0; x < image.Width; x++)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    //取包括本身在内的9个点。并且计算其平均值
                    int Red = 0, Blue = 0, Green = 0;
                    int PointCount = 0;
                    for (int i = -1; i <= 1; i++)
                    {
                        for (int j = -1; j <= 1; j++)
                        {
                            //有像素点的话，叠加
                            if (x + i >= 0 && y + j >= 0 && x + i < image.Width && y + j < image.Height)
                            {
                                currentPixel = image.GetPixel(x, y);
                                Red += Convert.ToInt32(currentPixel.R);
                                Blue += Convert.ToInt32(currentPixel.B);
                                Green += Convert.ToInt32(currentPixel.G);
                                PointCount++;
                            }
                        }
                    }
                    Red = Red / PointCount;
                    Blue = Blue / PointCount;
                    Green = Green / PointCount;
                    //计算最小的欧式距离
                    double iDistance = 9999999; //默认无限大
                    int iX = x, iY = y; //欧式距离最小的那个色点，默认为本身
                    for (int i = -1; i <= 1; i++)
                    {
                        for (int j = -1; j <= 1; j++)
                        {
                            //有像素点的话，叠加
                            if (x + i >= 0 && y + j >= 0 && x + i < image.Width && y + j < image.Height)
                            {
                                currentPixel = image.GetPixel(x + i, y + j);
                                double iCurrentDistance = Math.Pow((Convert.ToDouble(currentPixel.R) - Red), 2) + Math.Pow(Convert.ToDouble(currentPixel.G) - Green, 2) + Math.Pow(Convert.ToDouble(currentPixel.B) - Blue, 2);
                                if (iCurrentDistance < iDistance)
                                {
                                    iX = x + i;
                                    iY = y + j;
                                    iDistance = iCurrentDistance;
                                }
                            }
                        }
                    }
                    imageResult.SetPixel(x, y, image.GetPixel(iX, iY));
                }
            }
            imageResult.Save("jiangzao.jpg");
            return imageResult;
        }
        /// <summary>
        /// 去除边框
        /// </summary>
        /// <param name="imageSrc">源图像</param>
        /// <param name="iBorderWidth">边框</param>
        /// <returns></returns>
        public static Bitmap deleteBorder(Bitmap imageSrc, int iBorderWidth)
        {
            Bitmap image = new Bitmap(imageSrc.Width - 2 * iBorderWidth, imageSrc.Height - 2 * iBorderWidth);
            //复制边框以外的图像到新图像
            Rectangle rec = new Rectangle(iBorderWidth, iBorderWidth, imageSrc.Width - iBorderWidth * 2, imageSrc.Height - iBorderWidth * 2);
            image = imageSrc.Clone(rec, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            return image;
        }

        /// <summary>
        /// 黑白去噪
        /// </summary>
        /// <param name="imageSrc">源图像</param>
        /// <param name="iPointWidth">像素宽度</param>
        /// <param name="iThreshold">阈值</param>
        /// <returns>处理后图像</returns>
        public static Bitmap blackClear(Bitmap imageSrc, int iPointWidth, int iThreshold)
        {
            //检查像素点，如果为白色0，跳过，如果为黑色1则进行噪点处理
            //统计周围的像素点，如果白色的个数超过阈值，则认为是噪点，将其设置为白色。
            Color color;
            Bitmap image = new Bitmap(imageSrc.Width + 2 * iPointWidth, imageSrc.Height + 2 * iPointWidth);
            //设置为全白，并拷贝图像
            Graphics g = Graphics.FromImage(image);
            g.Clear(Color.White);
            g.DrawImage(imageSrc, iPointWidth, iPointWidth);
            g.Dispose();
            Bitmap imageResult = new Bitmap(image);

            for (int x = iPointWidth - 1; x < image.Width - iPointWidth + 1; x++)
            {
                for (int y = iPointWidth - 1; y < image.Height - iPointWidth + 1; y++)
                {

                    color = image.GetPixel(x, y);
                    int iWhiteCount = 0;
                    if (color.ToArgb() == Color.Black.ToArgb()) //黑色的话进行检查边上的。注意颜色对象不能直接比较。必须转换一下
                    {
                        for (int i = -iPointWidth; i <= iPointWidth; i++)
                        {
                            for (int j = -iPointWidth; j <= iPointWidth; j++)
                            {
                                //有像素点的话，统计
                                if (x + i >= 0 && y + j >= 0 && x + i < image.Width && y + j < image.Height)
                                {
                                    //该边际点为白色，且不是自身
                                    if (image.GetPixel(x + i, y + j).ToArgb() == Color.White.ToArgb() && i != 0 && j != 0)
                                    {
                                        iWhiteCount++;
                                    }
                                }
                            }
                        }
                        if (iWhiteCount > iThreshold) //白色点数大于阈值，认为是噪点
                        {
                            imageResult.SetPixel(x, y, Color.White);
                        }

                    }
                }
            }
            image.Dispose();
            imageResult.Save("heibai.jpg");
            return imageResult;
        }
        #endregion
    }
}
