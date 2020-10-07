using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ZXing.QrCode;
using ZXing;
using ZXing.Common;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Threading;

namespace ConsoleFX
{
    class Program
    {  // 10进制转换成36进制
        public static string int10Convert36(int i)
        {

            string str = "";
            while (i > 35)
            {
                int j = i % 36;
                str += ((j <= 9) ? Convert.ToChar(j + '0') : Convert.ToChar(j - 10 + 'A'));
                i = i / 36;
            }
            str += ((i <= 9) ? Convert.ToChar(i + '0') : Convert.ToChar(i - 10 + 'A'));

            Char[] c = str.ToCharArray();
            Array.Reverse(c);
            return new string(c);
        }
        static void Main(string[] args)
        {
            #region 生成激活码
            //    List<string> codes = new List<string>();
            //    int count = 0;
            //    while (codes.Count <500)
            //    {
            //        byte[] buffer = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));

            //        var s = BitConverter.ToInt64(buffer, 0);
            //        var re = new BaseConversionHelper().SixtyTwoScale(Convert.ToInt64(s));

            //        if (codes.Contains(re))
            //        {
            //            ConsoleColor currentForeColor = Console.ForegroundColor;
            //            Console.ForegroundColor = ConsoleColor.Red;
            //            count++;
            //            Console.WriteLine($"{s}重复{re},重复总次数{count}");
            //            Console.ForegroundColor = currentForeColor;
            //        }
            //        else
            //            codes.Add(re);
            //        Console.WriteLine($"原值{s}新值{re}");
            //    }
            //    Console.WriteLine($"{codes.Count}重复总次数{count}");
            //    count = 0;
            //    while (codes.Count < 2000)
            //    {
            //        byte[] buffer = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));

            //        var s = BitConverter.ToInt32(buffer, 0); 
            //        var re = new BaseConversionHelper().SixtyTwoScale(Convert.ToInt64(s));

            //        if (codes.Contains(re))
            //        {
            //            ConsoleColor currentForeColor = Console.ForegroundColor;
            //            Console.ForegroundColor = ConsoleColor.Red;
            //            count++;
            //            Console.WriteLine($"{s}重复{re},重复总次数{count}");
            //            Console.ForegroundColor = currentForeColor;
            //        }
            //        else
            //            codes.Add(re);
            //        Console.WriteLine($"原值{s}新值{re}");
            //    }
            //    Console.WriteLine($"{codes.Count}重复总次数{count}");
            #endregion

            //  new kuijichafen().JieXi(File.ReadAllText("C:/Users/admin/Desktop/全国会计资格评价网.html"));

            while (true)
            {  
                new kuijichafen().Get("522225197410140035", "李万全", "34");
                Thread.Sleep(1000);
                new kuijichafen().Get("412829198802044488", "胡雪媛", "26", 1);
                new kuijichafen().Get("372930197711291882", "马绍玲", "25");
                Thread.Sleep(1000);
                new kuijichafen().Get("411424200002146246", "郑爽爽", "26");
                Thread.Sleep(1000);
          
                Console.ReadLine();
            }
            // RecognitionImage();
            //   SimilarSearchDemo();
            // SameHqSearchDemo();
            Console.ReadLine();
        }
        public static void SameHqSearchDemo()
        {  // 设置APPID/AK/SK
            var APP_ID = "21420045";
            var API_KEY = "YdWwajRotuPVdCMc35qB1U7S";
            var SECRET_KEY = "ePIaisz22vZungzdCaIuOumYKiVYObME";

            var client = new Baidu.Aip.ImageSearch.ImageSearch(API_KEY, SECRET_KEY);
            client.Timeout = 60000;  // 修改超时时间
            var image = File.ReadAllBytes(@"C:\Users\admin\Desktop\微信图片_20200716163059.jpg");
            // 调用相同图检索—检索, 图片参数为本地图片，可能会抛出网络等异常，请使用try/catch捕获
            var result = client.SameHqSearch(image);
            Console.WriteLine(result);
            // 如果有可选参数
            var options = new Dictionary<string, object>{

        {"pn", "0"},
        {"rn", "250"}
    };
            // 带参数调用相同图检索—检索, 图片参数为本地图片
            result = client.SameHqSearch(image, options);
            Console.WriteLine(result);
        }
        /// <summary>
        /// 文字识别
        /// </summary>
        static void RecognitionImage()
        {
            // 设置APPID/AK/SK
            var APP_ID = "21512957";
            var API_KEY = "OFez8onF6CqQ8khtNnecrWGD";
            var SECRET_KEY = "2aAt05myciGn6DvGPGLAL8LQ5v4kMm56";

            var client = new Baidu.Aip.Ocr.Ocr(API_KEY, SECRET_KEY);
            client.Timeout = 60000;  // 修改超时时间
            var url = "https://ai.bdstatic.com/file/3986A65C9EDA45AF8877DD42D5256403";

            //        // 调用通用文字识别, 图片参数为远程url图片，可能会抛出网络等异常，请使用try/catch捕获
            //        var result = client.GeneralBasicUrl(url);
            //        Console.WriteLine(result);
            //        // 如果有可选参数
            var options = new Dictionary<string, object>{
        {"language_type", "CHN_ENG"},
        {"detect_direction", "true"},
        {"detect_language", "true"},
        {"probability", "true"}
    };
            //        // 带参数调用通用文字识别, 图片参数为远程url图片
            //        result = client.GeneralBasicUrl(url, options);
            //        Console.WriteLine(result);

            var image = File.ReadAllBytes(@"C:\Users\admin\Desktop\微信图片_20200930104921.png");
            // 调用通用文字识别, 图片参数为本地图片，可能会抛出网络等异常，请使用try/catch捕获
            var result1 = client.GeneralBasic(image);
            Console.WriteLine(result1);
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            // 带参数调用通用文字识别, 图片参数为本地图片
            var result = client.GeneralBasic(image, options);
            Console.WriteLine(result);
        }
        public static void SimilarSearchDemo()
        {   // 设置APPID/AK/SK
            var APP_ID = "21420045";
            var API_KEY = "YdWwajRotuPVdCMc35qB1U7S";
            var SECRET_KEY = "ePIaisz22vZungzdCaIuOumYKiVYObME";

            var client = new Baidu.Aip.ImageSearch.ImageSearch(API_KEY, SECRET_KEY);
            client.Timeout = 60000;  // 修改超时时间
            var image = File.ReadAllBytes(@"C:\Users\admin\Desktop\微信图片_20200716163059.jpg");
            // 调用相似图检索—检索, 图片参数为本地图片，可能会抛出网络等异常，请使用try/catch捕获
            var result = client.SimilarSearch(image);
            Console.WriteLine(result);
            // 如果有可选参数
            var options = new Dictionary<string, object>{

        {"pn", "0"},
        {"rn", "1"}
    };
            // 带参数调用相似图检索—检索, 图片参数为本地图片
            result = client.SimilarSearch(image, options);
            Console.WriteLine(result);
        }

        /// <summary>
        /// 会产生graphics异常的PixelFormat
        /// </summary>
        private static PixelFormat[] indexedPixelFormats = { PixelFormat.Undefined, PixelFormat.DontCare,
PixelFormat.Format16bppArgb1555, PixelFormat.Format1bppIndexed, PixelFormat.Format4bppIndexed,
PixelFormat.Format8bppIndexed
    };
        /// <summary>
        /// 判断图片的PixelFormat 是否在 引发异常的 PixelFormat 之中
        /// </summary>
        /// <param name="imgPixelFormat">原图片的PixelFormat</param>
        /// <returns></returns>
        private static bool IsPixelFormatIndexed(PixelFormat imgPixelFormat)
        {
            foreach (PixelFormat pf in indexedPixelFormats)
            {
                if (pf.Equals(imgPixelFormat)) return true;
            }

            return false;
        }
        static void Qrcode()
        {
            string path = "mycode.jpg";// "fc2411684f96466684d667c2aa5d38a3.png";
            Bitmap myImage = Image.FromFile(path) as Bitmap;
            Console.WriteLine("请输入地址");
            string url = Console.ReadLine();
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
            request.Method = "Get";
            Bitmap oriBitmap = Image.FromFile(@"e92bda7fa55c4f41a8e3dc65ce56755f.png") as Bitmap;
            using (var stream = request.GetResponse().GetResponseStream())
            {
                oriBitmap = Image.FromStream(stream) as Bitmap;
            }
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            var source = new BitmapLuminanceSource(oriBitmap);
            var bitmap = new BinaryBitmap(new HybridBinarizer(source));
            QRCodeReader reader = new QRCodeReader();
            var result = reader.decode(bitmap);
            if (result != null)
            {
                Console.WriteLine($"二维码内容{result.Text}");
                var dsd = result.ResultMetadata;
                foreach (var point in result.ResultPoints)
                {
                    Console.WriteLine($"二维码坐标：{point.X},{point.Y}");

                }

                float point1X = result.ResultPoints[0].X;
                float point1Y = result.ResultPoints[0].Y;

                float point2X = result.ResultPoints[1].X;
                float point2Y = result.ResultPoints[1].Y;

                var rect = new Rectangle();
                rect.X = (int)result.ResultPoints[1].X;
                rect.Y = (int)result.ResultPoints[1].Y;
                rect.Width = (int)Math.Abs(result.ResultPoints[1].X - result.ResultPoints[2].X);
                rect.Height = (int)Math.Abs(result.ResultPoints[0].Y - result.ResultPoints[1].Y);

                Bitmap bmp = oriBitmap;
                //如果原图片是索引像素格式之列的，则需要转换
                if (IsPixelFormatIndexed(oriBitmap.PixelFormat))
                {
                    bmp = new Bitmap(oriBitmap.Width, oriBitmap.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.DrawImage(oriBitmap, 0, 0);
                    }

                }

                Bitmap blockBitmap = cutImage(bmp, new Point((int)point1X, (int)point1Y), 100, 100);

                //  blockBitmap.Save("block.png");
                var pixs = GetPixs(blockBitmap);
                int index = 0;
                int whiteCount = 0;
                for (int x = 0; x < pixs[0].Length; x++)
                {
                    if (x > 0 && pixs[0][x] != pixs[0][x - 1])
                    {
                        whiteCount += 1;
                    }
                    if (whiteCount == 3)
                    {
                        index = x;
                        break;
                    }
                }
                rect.X -= index;
                rect.Y -= index;
                rect.Height += 2 * index;
                rect.Width += 2 * index;
                using (Graphics g = Graphics.FromImage(bmp))
                {

                    g.DrawImage(myImage, rect);
                    g.DrawRectangle(new Pen(new SolidBrush(Color.Red), 2), rect);
                }
                stopwatch.Stop();
                Console.WriteLine("耗时" + stopwatch.ElapsedMilliseconds);
                bmp.Save("newimage.png");
                blockBitmap.Dispose();
            }
            else
            {
                Console.WriteLine("识别失败");
            }
        }

        static Point[] GetMinPoint(ResultPoint[] resultPoints)
        {
            return null;
        }
        /// <summary>
        /// 截取图片区域，返回所截取的图片
        /// </summary>
        /// <param name="SrcImage"></param>
        /// <param name="pos"></param>
        /// <param name="cutWidth"></param>
        /// <param name="cutHeight"></param>
        /// <returns></returns>
        private static Bitmap cutImage(Bitmap SrcImage, Point pos, int cutWidth, int cutHeight)
        {

            Bitmap cutedImage = null;

            //先初始化一个位图对象，来存储截取后的图像
            Bitmap bmpDest = new Bitmap(cutWidth, cutHeight, PixelFormat.Format32bppRgb);
            Graphics g = Graphics.FromImage(bmpDest);

            //矩形定义,将要在被截取的图像上要截取的图像区域的左顶点位置和截取的大小
            Rectangle rectSource = new Rectangle(pos.X, pos.Y, cutWidth, cutHeight);


            //矩形定义,将要把 截取的图像区域 绘制到初始化的位图的位置和大小
            //rectDest说明，将把截取的区域，从位图左顶点开始绘制，绘制截取的区域原来大小
            Rectangle rectDest = new Rectangle(0, 0, cutWidth, cutHeight);

            //第一个参数就是加载你要截取的图像对象，第二个和第三个参数及如上所说定义截取和绘制图像过程中的相关属性，第四个属性定义了属性值所使用的度量单位
            g.DrawImage(SrcImage, rectDest, rectSource, GraphicsUnit.Pixel);

            //在GUI上显示被截取的图像
            cutedImage = bmpDest;

            g.Dispose();

            return cutedImage;

        }
        /// <summary>
        /// 二值化图片
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static short[][] GetPixs(Bitmap bitmap)
        {
            int height = bitmap.Height;
            int width = bitmap.Width;
            byte tempB, tempG, tempR;
            short[][] spOriginData = new short[height][];
            for (int i = 0; i < height; i++)
            {
                spOriginData[i] = new short[width];
            }

            BitmapData dataOut = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            int offset = dataOut.Stride - dataOut.Width * 3;
            try
            {
                unsafe
                {
                    byte* pOut = (byte*)(dataOut.Scan0.ToPointer());

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            tempB = pOut[0];
                            tempG = pOut[1];
                            tempR = pOut[2];
                            double data = 0.31 * tempR + 0.59 * tempG + 0.11 * tempB;
                            if (data > 255)
                                spOriginData[y][x] = 255;
                            else
                                if (data < 0)
                                spOriginData[y][x] = 0;
                            else
                                spOriginData[y][x] = (short)(data > 127 ? 255 : 0);
                            pOut += 3;
                        }
                        pOut += offset;
                    }
                    bitmap.UnlockBits(dataOut);
                }
            }
            catch
            {
            }
            return spOriginData;
        }

        static void Test()
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
