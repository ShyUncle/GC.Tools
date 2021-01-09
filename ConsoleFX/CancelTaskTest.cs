using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleFX
{
    public class CancelTaskTest
    {
        public static CancellationTokenSource cts;
        public static Task Start()
        {
            cts = new CancellationTokenSource();

            var task = Task.Factory.StartNew(() =>
              {
                  try
                  {
                      while (true)//!cts.IsCancellationRequested)
                        {
                          cts.Token.ThrowIfCancellationRequested();
                          Thread.Sleep(1000);
                          Console.WriteLine(DateTime.Now);
                      }
                  }
                  catch (OperationCanceledException ex)
                  {
                      Console.WriteLine(ex.ToString());

                      Console.WriteLine("取消成功");
                  }
              }, cts.Token);

            return task; 
        }

        public static void Stop()
        {
            if (cts != null)
            {
                Console.WriteLine("开始取消");
                cts.Cancel();
            }
        }
    }
}
