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
using ConsoleFX;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using static CL.Utility.Program;
using iText.Kernel.Geom;

namespace CL.Utility
{
    class Program
    {
        static void Main(string[] args)
        {
            // PDFRead();
            PDFTxtRead();
            Console.WriteLine("ok");
            Console.ReadLine();
        }
        //整理txt
        //—\d+—.*去掉页码
        //^\s*\r?\n去掉空行
        /// <summary>
        /// 广西
        /// </summary>
        public static void GXPDFTxtRead()
        {
            const string xuexiaoreg = "(\\d{4,4})([\\u4e00-\\u9fa5]+[\\r\\n]*?.*[香港市]\\))";
            const string zhuanyereg = "\\b(\\d{2}|[ab]\\d)([\\u4e00-\\u9fa5]{2,}[\\S\\s]*?)\\s*(\\d+)([一二三四五两六七八九]年医*)(\\d+)";
            //PDF模板路径
            string loadpath = "E:\\download\\pdf-to-txt-python-master\\gx1.txt";
            string txtPaht = "E:\\download\\pdf-to-txt-python-master\\gx3.txt";
            var lines = File.ReadAllLines(loadpath);
            var str = File.ReadAllText(loadpath);
            var list = new List<Xuexiao>();

            StringBuilder newStr = new StringBuilder();
            Dictionary<int, Xuexiao> piciDic = new Dictionary<int, Xuexiao>();

            #region 批次
            var index = str.IndexOf("(一)提前录取普通院校");
            Xuexiao pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "提前录取普通院校";
            piciDic.Add(index, pici);
            index = str.IndexOf("(二)提前录取军队院校");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "提前录取军队院校";
            piciDic.Add(index, pici);

            index = str.IndexOf("(三)提前录取招飞(民航)院校");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "提前录取招飞(民航)院校";
            piciDic.Add(index, pici);
            index = str.IndexOf("(四)提前录取公安、司法、消防院校");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "提前录取公安、司法、消防院校";
            piciDic.Add(index, pici);
            index = str.IndexOf("2.公安专科院校");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "公安专科院校";
            piciDic.Add(index, pici);
            index = str.IndexOf("3.面向贫困地区定向招生本科专项计划(国家专项)");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "面向贫困地区定向招生本科专项计划(国家专项)";
            piciDic.Add(index, pici);
            index = str.IndexOf("1.特殊类型招生");
            pici = new Xuexiao();
            pici.Pici = "普通本科第一批";
            pici.ABduan = "1.特殊类型招生";
            piciDic.Add(index, pici);
            index = str.IndexOf("2.A段");
            pici = new Xuexiao();
            pici.Pici = "普通本科第一批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);
            index = str.IndexOf("3.B段");
            pici = new Xuexiao();
            pici.Pici = "普通本科第一批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);

            index = str.IndexOf("1.A段");
            pici = new Xuexiao();
            pici.Pici = "普通本科第二批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);
            index = str.IndexOf("2.B段");
            pici = new Xuexiao();
            pici.Pici = "普通本科第二批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);
            index = str.IndexOf("体育类本科批");
            pici = new Xuexiao();
            pici.Pici = "体育类本科批";
            pici.ABduan = "体育类本科批";
            piciDic.Add(index, pici);

            index = str.IndexOf("2.体育类高职(专科)批");
            pici = new Xuexiao();
            pici.Pici = "体育类高职(专科)批";
            pici.ABduan = "体育类高职(专科)批";
            piciDic.Add(index, pici);
            index = str.IndexOf("(1)A段");
            pici = new Xuexiao();
            pici.Pici = "艺术类本科提前批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);
            index = str.IndexOf("(2)B段");
            pici = new Xuexiao();
            pici.Pici = "艺术类本科提前批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);

            index = str.IndexOf("2.艺术类本科批A段");
            pici = new Xuexiao();
            pici.Pici = "艺术类本科批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);

            index = str.IndexOf("3.艺术类本科批B段");
            pici = new Xuexiao();
            pici.Pici = "艺术类本科批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);
            index = str.IndexOf("4.艺术类高职(专科)批A段");
            pici = new Xuexiao();
            pici.Pici = "艺术类高职(专科)批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);
            index = str.IndexOf("5.艺术类高职(专科)批B段");
            pici = new Xuexiao();
            pici.Pici = "艺术类高职(专科)批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);


            index = str.IndexOf("八、高职(专科)提前批");
            pici = new Xuexiao();
            pici.Pici = "高职(专科)提前批";
            pici.ABduan = "高职(专科)提前批";
            piciDic.Add(index, pici);
            index = str.IndexOf("九、普通高职(专科)批");
            pici = new Xuexiao();
            pici.Pici = "普通高职(专科)批";
            pici.ABduan = "普通高职(专科)批";
            piciDic.Add(index, pici);
            #endregion

            Xuexiao xuexiao = null;
            var listMatch = Regex.Matches(str, xuexiaoreg, RegexOptions.Multiline);
            foreach (Match item in listMatch)
            {
                xuexiao = new Xuexiao()
                {
                    Pici = pici.Pici,
                    ABduan = pici.ABduan,
                    Index = item.Index,
                    Code = item.Groups[1].Value,
                    Name = item.Groups[2].Value.Replace("\n", "").Replace("\r", ""),
                    Zhuanyes = new List<Zhuanye>(),
                };
                var piciItem = piciDic.Where(x => x.Key <= item.Index).LastOrDefault();
                if (piciItem.Value != null)
                {
                    xuexiao.Pici = piciItem.Value.Pici;
                    xuexiao.ABduan = piciItem.Value.ABduan;
                }
                list.Add(xuexiao);
                var next = item.NextMatch();
                string piceces = "";
                if (next != null && next.Success)
                {
                    piceces = str.Substring(item.Index, next.Index - item.Index);
                }
                else
                {
                    piceces = str.Substring(item.Index);
                }
                if (piceces.Contains("注:"))
                {
                    xuexiao.Beizhu = piceces.Substring(piceces.IndexOf("注:")).Replace("\n", "").Replace("\r", "");
                }
                var wenshiIndex = piceces.IndexOf("文史类:");
                var ligongIndex = piceces.IndexOf("理工类:");
                if (wenshiIndex < 0 && ligongIndex < 0)
                {
                    wenshiIndex = piceces.IndexOf("艺术文");
                    ligongIndex = piceces.IndexOf("艺术理");
                }
                var regValue = Regex.Matches(piceces, zhuanyereg);
                foreach (Match zyitem in regValue)
                {
                    var zy = new Zhuanye()
                    {
                        Code = zyitem.Groups[1].Value,
                        Name = zyitem.Groups[2].Value.Replace("\n", "").Replace("\r", ""),
                        Renshu = zyitem.Groups[3].Value,
                        Xuenian = zyitem.Groups[4].Value,
                        Xuefei = zyitem.Groups[5].Value,

                    };
                    if (ligongIndex >= 0 && zyitem.Index > ligongIndex && zyitem.Index < wenshiIndex && wenshiIndex >= 0)
                    {
                        zy.Wenli = "理";
                    }
                    else if (zyitem.Index >= wenshiIndex && wenshiIndex >= 0)
                    {
                        zy.Wenli = "文";
                    }
                    else if (ligongIndex >= 0 && zyitem.Index > ligongIndex && wenshiIndex < 0)
                    {
                        zy.Wenli = "理";
                    }
                    xuexiao.Zhuanyes.Add(zy);
                }
            }
            File.WriteAllText(txtPaht, Newtonsoft.Json.JsonConvert.SerializeObject(list));
            Console.WriteLine("完成");
        }

        /// <summary>
        /// 黑龙江
        /// </summary>
        public static void PDFTxtRead()
        {
            const string xuexiaoreg = "(\\d{4,4})([\\u4e00-\\u9fa5]+[\\r\\n]*?.*[香港市]\\))";
            const string zhuanyereg = "\\b(\\d{2}|[ab]\\d)([\\u4e00-\\u9fa5]{2,}[\\S\\s]*?)\\s*(\\d+)([一二三四五两六七八九]年医*)(\\d+)";
            //PDF模板路径
            string loadpath = "E:\\download\\pdf-to-txt-python-master\\1.txt";
            string txtPaht = "E:\\download\\pdf-to-txt-python-master\\3.txt";
            var lines = File.ReadAllLines(loadpath);
            var str = File.ReadAllText(loadpath);
            var list = new List<Xuexiao>();

            StringBuilder newStr = new StringBuilder();
            Dictionary<int, Xuexiao> piciDic = new Dictionary<int, Xuexiao>();

            #region 批次
            var index = str.IndexOf("(一)提前录取普通院校");
            Xuexiao pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "提前录取普通院校";
            piciDic.Add(index, pici);
            index = str.IndexOf("(二)提前录取军队院校");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "提前录取军队院校";
            piciDic.Add(index, pici);

            index = str.IndexOf("(三)提前录取招飞(民航)院校");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "提前录取招飞(民航)院校";
            piciDic.Add(index, pici);
            index = str.IndexOf("(四)提前录取公安、司法、消防院校");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "提前录取公安、司法、消防院校";
            piciDic.Add(index, pici);
            index = str.IndexOf("2.公安专科院校");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "公安专科院校";
            piciDic.Add(index, pici);
            index = str.IndexOf("3.面向贫困地区定向招生本科专项计划(国家专项)");
            pici = new Xuexiao();
            pici.Pici = "提前批";
            pici.ABduan = "面向贫困地区定向招生本科专项计划(国家专项)";
            piciDic.Add(index, pici);
            index = str.IndexOf("1.特殊类型招生");
            pici = new Xuexiao();
            pici.Pici = "普通本科第一批";
            pici.ABduan = "1.特殊类型招生";
            piciDic.Add(index, pici);
            index = str.IndexOf("2.A段");
            pici = new Xuexiao();
            pici.Pici = "普通本科第一批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);
            index = str.IndexOf("3.B段");
            pici = new Xuexiao();
            pici.Pici = "普通本科第一批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);

            index = str.IndexOf("1.A段");
            pici = new Xuexiao();
            pici.Pici = "普通本科第二批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);
            index = str.IndexOf("2.B段");
            pici = new Xuexiao();
            pici.Pici = "普通本科第二批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);
            index = str.IndexOf("体育类本科批");
            pici = new Xuexiao();
            pici.Pici = "体育类本科批";
            pici.ABduan = "体育类本科批";
            piciDic.Add(index, pici);

            index = str.IndexOf("2.体育类高职(专科)批");
            pici = new Xuexiao();
            pici.Pici = "体育类高职(专科)批";
            pici.ABduan = "体育类高职(专科)批";
            piciDic.Add(index, pici);
            index = str.IndexOf("(1)A段");
            pici = new Xuexiao();
            pici.Pici = "艺术类本科提前批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);
            index = str.IndexOf("(2)B段");
            pici = new Xuexiao();
            pici.Pici = "艺术类本科提前批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);

            index = str.IndexOf("2.艺术类本科批A段");
            pici = new Xuexiao();
            pici.Pici = "艺术类本科批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);

            index = str.IndexOf("3.艺术类本科批B段");
            pici = new Xuexiao();
            pici.Pici = "艺术类本科批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);
            index = str.IndexOf("4.艺术类高职(专科)批A段");
            pici = new Xuexiao();
            pici.Pici = "艺术类高职(专科)批";
            pici.ABduan = "A段";
            piciDic.Add(index, pici);
            index = str.IndexOf("5.艺术类高职(专科)批B段");
            pici = new Xuexiao();
            pici.Pici = "艺术类高职(专科)批";
            pici.ABduan = "B段";
            piciDic.Add(index, pici);


            index = str.IndexOf("八、高职(专科)提前批");
            pici = new Xuexiao();
            pici.Pici = "高职(专科)提前批";
            pici.ABduan = "高职(专科)提前批";
            piciDic.Add(index, pici);
            index = str.IndexOf("九、普通高职(专科)批");
            pici = new Xuexiao();
            pici.Pici = "普通高职(专科)批";
            pici.ABduan = "普通高职(专科)批";
            piciDic.Add(index, pici);
            #endregion

            Xuexiao xuexiao = null;
            var listMatch = Regex.Matches(str, xuexiaoreg, RegexOptions.Multiline);
            foreach (Match item in listMatch)
            {
                xuexiao = new Xuexiao()
                {
                    Pici = pici.Pici,
                    ABduan = pici.ABduan,
                    Index = item.Index,
                    Code = item.Groups[1].Value,
                    Name = item.Groups[2].Value.Replace("\n", "").Replace("\r", ""),
                    Zhuanyes = new List<Zhuanye>(),
                };
                var piciItem = piciDic.Where(x => x.Key <= item.Index).LastOrDefault();
                if (piciItem.Value != null)
                {
                    xuexiao.Pici = piciItem.Value.Pici;
                    xuexiao.ABduan = piciItem.Value.ABduan;
                }
                list.Add(xuexiao);
                var next = item.NextMatch();
                string piceces = "";
                if (next != null && next.Success)
                {
                    piceces = str.Substring(item.Index, next.Index - item.Index);
                }
                else
                {
                    piceces = str.Substring(item.Index);
                }
                if (piceces.Contains("注:"))
                {
                    xuexiao.Beizhu = piceces.Substring(piceces.IndexOf("注:")).Replace("\n", "").Replace("\r", "");
                }
                var wenshiIndex = piceces.IndexOf("文史类:");
                var ligongIndex = piceces.IndexOf("理工类:");
                if (wenshiIndex < 0 && ligongIndex < 0)
                {
                    wenshiIndex = piceces.IndexOf("艺术文");
                    ligongIndex = piceces.IndexOf("艺术理");
                }
                var regValue = Regex.Matches(piceces, zhuanyereg);
                foreach (Match zyitem in regValue)
                {
                    var zy = new Zhuanye()
                    {
                        Code = zyitem.Groups[1].Value,
                        Name = zyitem.Groups[2].Value.Replace("\n", "").Replace("\r", ""),
                        Renshu = zyitem.Groups[3].Value,
                        Xuenian = zyitem.Groups[4].Value,
                        Xuefei = zyitem.Groups[5].Value,

                    };
                    if (ligongIndex >= 0 && zyitem.Index > ligongIndex && zyitem.Index < wenshiIndex && wenshiIndex >= 0)
                    {
                        zy.Wenli = "理";
                    }
                    else if (zyitem.Index >= wenshiIndex && wenshiIndex >= 0)
                    {
                        zy.Wenli = "文";
                    }
                    else if (ligongIndex >= 0 && zyitem.Index > ligongIndex && wenshiIndex < 0)
                    {
                        zy.Wenli = "理";
                    }
                    xuexiao.Zhuanyes.Add(zy);
                }
            }
            File.WriteAllText(txtPaht, Newtonsoft.Json.JsonConvert.SerializeObject(list));
            Console.WriteLine("完成");
        }

        public class Xuexiao
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string Beizhu { get; set; }
            public string Pici { get; set; }
            public string ABduan { get; set; }
            [JsonIgnore]
            public long Index { get; set; }
            public List<Zhuanye> Zhuanyes { get; set; }
        }

        public class Zhuanye
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string Renshu { get; set; }
            public string Xuefei { get; set; }
            public string Xuenian { get; set; }
            public string Wenli { get; set; }
            [JsonIgnore]
            public long Index { get; set; }
        }
    }
}
