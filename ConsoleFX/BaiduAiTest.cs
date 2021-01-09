using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleFX
{
  public  class BaiduAiTest
    {

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
    }
}
