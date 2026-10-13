using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AIConsoleApp
{
    public static class WeatherTools
    {
        /// <summary>
        /// 获取当前用户所在位置，返回省市信息。
        /// </summary>
        [Description("获取当前用户所在位置")]
        public static string GetLocation()
        {
            Console.WriteLine($"查询位置信息...");
            // 实际项目中可以替换成真实定位接口
            return "河南省郑州市";
        }

        /// <summary>
        /// 查询指定城市指定日期的天气。
        /// </summary>
        [Description("查询指定城市指定日期的天气")]
        public static string GetWeather([Description("城市名称，例如：北京、上海、郑州")] string location, [Description("指定的日期，日期格式yyyy-MM-dd")] DateTime date)
        {
            // 实际项目中可以调用天气 API
            Console.WriteLine($"查询 {location} {date:yyyy-MM-dd} 的天气信息...");
            return """
        河南省郑州市有小雨，气温 15~21℃。
        当前气温约 19℃，体感 18℃，北风较弱，湿度较高。
        空气质量轻度污染，AQI 约 107。
        """;
        }
    }
}
