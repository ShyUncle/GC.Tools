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
using System.Reflection;
using iTextSharp.text.pdf;

namespace CL.Utility
{
    class Program
    {
        static void Main(string[] args)
        {


            var sourcepath = System.Web.Hosting.HostingEnvironment.ApplicationPhysicalPath;
            //PDF模板路径
            string loadpath = "E:\\download" + "/New_Blank_Document.pdf";
            //PDF文件输出路径
            string outpath = "E:\\download" + "/oupput.pdf";


            //加载模板
            PdfReader reader = new PdfReader(loadpath);
            //文件输出流
            FileStream fFileStream = new FileStream(outpath, FileMode.Create);

            //进行PDF字段操作
            PdfStamper stamper = new PdfStamper(reader, fFileStream);
            AcroFields form = stamper.AcroFields;
            //填充PDF里的字段内容
            stamper.Writer.CloseStream = false;
            form.SetField("name", "a");

            //设置不可编辑
            stamper.FormFlattening = true;
            stamper.Close();

            iTextSharp.text.Document ManagementReportDoc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 15f, 15f, 75f, 75f);

            //FileStream file = new FileStream( "E://"   + DateTime.Now.ToString("dd-MMMM-yy") + ".pdf", System.IO.FileMode.OpenOrCreate);

            iTextSharp.text.pdf.PdfWriter writer = iTextSharp.text.pdf.PdfWriter.GetInstance(ManagementReportDoc, fFileStream); // PdfWriter.GetInstance(ManagementReportDoc, file);

            ManagementReportDoc.Open();
            //// step 4 将一个元素添加到文档中
            ManagementReportDoc.Add(new iTextSharp.text.Paragraph("Hello World!"));
            //// step 5 关闭文档 
            ManagementReportDoc.Close();
            Console.WriteLine("ok");
            Console.ReadLine();
        }

        public class Animal
        {
            public virtual void J()
            {
                Console.WriteLine("叫一声");
            }
            public void WangWang()
            {
                J();
            }
        }

        public class Dog : Animal
        {
            public override void J()
            {
                Console.WriteLine("狗叫");
            }
        }
        public class Bandiangou : Dog
        {

        }

        #region 取消线程测试

        #endregion

        #region 二维码定位
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
        #endregion

        #region 图片压缩

        #endregion
    }
}
