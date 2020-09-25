using NPOI.XWPF.UserModel;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GC.Tools.NPOITest
{
    public class NpoiWordHelper
    {
        public void ReadWorld(string path)
        {
            Stream stream = File.OpenRead(path);
            XWPFDocument doc = new XWPFDocument(stream);
            foreach (var para in doc.Paragraphs)
            {
                string text = para.ParagraphText; //获得文本
                if (text.Trim() != "")
                {
                    Console.WriteLine("<p >");
                    var runss = para.Runs[0];

                    if (para.Runs[0].IsBold)
                    {
                        Console.WriteLine("<strong>");
                    }
                   
                    Console.WriteLine(text);
                    if (para.Runs[0].IsBold)
                    {
                        Console.WriteLine("</strong>");
                    }
                    Console.WriteLine("</p>");
                }

                if (para.Runs.Count > 0)
                {
                    var run = para.Runs[0];
                    var data = run.GetEmbeddedPictures();
                    foreach (var item in data)
                    {
                        var imgData = item.GetPictureData();
                        var img = GetImageFromByte(imgData.Data);
                        img.Save(imgData.FileName, ImageFormat.Jpeg);
                        Console.WriteLine("图片" + imgData.FileName);
                    }
                }

            }
            stream.Dispose();
        }

        public System.Drawing.Image GetImageFromByte(byte[] streamByte)
        {
            System.IO.MemoryStream ms = new System.IO.MemoryStream(streamByte);
            System.Drawing.Image img = System.Drawing.Image.FromStream(ms);
            return img;
        }
    }
}
