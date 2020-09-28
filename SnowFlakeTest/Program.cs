using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowFlakeTest
{
    class Program
    {
        // 这里必须单例，才能保证ID唯一
        // 不同功能用不同的workerId，可以更能保证ID唯一
        public static IdWorker worker = new IdWorker(1, 1);
        static void Main(string[] args)
        {
            var h = new ConcurrentDictionary<long, int>();

            Stopwatch st = new Stopwatch();
            st.Start();
            int count = 1;
            var r = Parallel.For(0, 1000, (i) =>
              {
                  Console.WriteLine(worker.NextId());
              });
            if (r.IsCompleted)
            {
                st.Stop();
                Console.WriteLine("生成完成，用时" + st.ElapsedMilliseconds + "毫秒");
                Console.ReadLine();
            }
        }
    }
}
