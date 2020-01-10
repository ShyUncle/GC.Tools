using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleFX
{
    public class FFMPEG
    {
        public void StartMerge()
        {
            var paths = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory + "\\后端开发", "filelist.txt", SearchOption.AllDirectories);
            foreach (var item in paths)
            {
                var parentdirectory = Directory.GetParent(item);

                if (File.Exists(Path.Combine(parentdirectory.Parent.FullName, parentdirectory.Name + ".mp4")))
                {
                    Directory.Delete(parentdirectory.FullName, true);
                    continue;
                }
                hasError = false;
                Process p = new Process();//建立外部调用线程
                p.StartInfo.WorkingDirectory = parentdirectory.FullName;
                p.StartInfo.FileName = @"ffmpeg.exe";//要调用外部程序的绝对路径
                p.StartInfo.Arguments = " -f concat -safe 0 -i filelist.txt -c copy " + Path.Combine(parentdirectory.Parent.FullName, parentdirectory.Name + ".mp4");
                p.StartInfo.UseShellExecute = false;//不使用操作系统外壳程序启动线程(一定为FALSE,详细的请看MSDN)
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = false;
                p.StartInfo.CreateNoWindow = false;//不创建进程窗口
                p.Start();//启动线程
                p.BeginErrorReadLine();
                //p.BeginOutputReadLine();
                p.ErrorDataReceived += new DataReceivedEventHandler(NetErrorDataHandler);
                p.WaitForExit();//阻塞等待进程结束

                p.Close();//关闭进程
                p.Dispose();//释放资源

                Console.WriteLine(item + "合成完成");
                if (!hasError)
                {
                    Directory.Delete(parentdirectory.FullName, true);
                }
            }
        }
        bool hasError = false;
        private void NetErrorDataHandler(object sendingProcess, DataReceivedEventArgs errLine)
        {
            if (!string.IsNullOrEmpty(errLine.Data))
            {
                hasError = true;
                Console.WriteLine(errLine.Data);
            }
        }

        private void NetOutputDataHandler(object sendingProcess, DataReceivedEventArgs errLine)
        {
            if (!string.IsNullOrEmpty(errLine.Data))
            {
                Console.WriteLine(errLine.Data);
            }
        }
    }
}
