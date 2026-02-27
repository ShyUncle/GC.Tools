using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatMsg
{
    public partial class Form1 : Form
    {
        private const int WM_COPYDATA = 0x004A;
        private IntPtr _weChatHandle;
        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int SendMessage(IntPtr hWnd, int msg, int wParam, ref COPYDATASTRUCT lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
            [DllImport("WeChatHook.dll",
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "InstallHook")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool InstallHook(IntPtr hReceiver, uint dwThreadId, uint uFilterMsg);

            [DllImport("WeChatHook.dll",
                CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "UninstallHook")]
            public static extern void UninstallHook();
        }

        public Form1()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 查找微信主窗体句柄（PC 版微信需已启动）
        /// </summary>
        private void btnFindWeChat_Click(object? sender, EventArgs e)
        {
            // 常见的 PC 微信主窗体类名为 WeChatMainWndForPC
            IntPtr hWnd = NativeMethods.FindWindow("WeChatMainWndForPC", null);
       
            // 如果通过类名没有找到，可以退回到标题匹配（简体中文标题“微信”）
            if (hWnd == IntPtr.Zero)
            {
                hWnd = NativeMethods.FindWindow(null, "微信");
            }

            if (hWnd == IntPtr.Zero)
            {
                AppendLog("未找到微信窗体句柄，请确认已登录 PC 版微信。");
            }
            else
            {
                _weChatHandle = hWnd;
                AppendLog($"找到微信窗体句柄: 0x{hWnd.ToInt64():X}");
                
                // BringWindowToFront(_weChatHandle);

                // 安装 WeChatHook 钩子：只针对微信主窗口所属线程，监听 WM_COPYDATA
                uint processId;
                uint threadId = NativeMethods.GetWindowThreadProcessId(_weChatHandle, out processId);

                if (threadId == 0)
                {
                    AppendLog("获取微信主窗体线程 ID 失败，无法安装钩子。");
                }
                else
                {
                    bool hooked = NativeMethods.InstallHook(this.Handle, threadId, WM_COPYDATA);
                    AppendLog(hooked
                        ? $"WeChatHook 安装成功，线程ID={threadId}，PID={processId}。"
                        : "WeChatHook 安装失败。");
                }
            }
        }

        /// <summary>
        /// 将指定窗口显示到前台
        /// </summary>
        private void BringWindowToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            // 恢复并激活窗口
            NativeMethods.ShowWindow(hWnd, SW_RESTORE);
            NativeMethods.SetForegroundWindow(hWnd);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                NativeMethods.UninstallHook();
            }
            catch
            {
                // 忽略卸载异常
            }

            base.OnFormClosing(e);
        }

        /// <summary>
        /// 向微信发送一条消息（参考博客：内容格式为 chatId + "||" + text，wParam=20001）
        /// </summary>
        private int SendWeChatMessage(string message, int wParam)
        {
            if (_weChatHandle == IntPtr.Zero)
            {
                AppendLog("尚未缓存微信窗体句柄，无法发送消息，请先点击“查找微信窗体句柄”。");
                return 0;
            }

            if (string.IsNullOrEmpty(message))
            {
                return 0;
            }

            byte[] sarr = Encoding.Default.GetBytes(message);
            IntPtr lpData = Marshal.AllocHGlobal(sarr.Length + 1);
            try
            {
                Marshal.Copy(sarr, 0, lpData, sarr.Length);
                Marshal.WriteByte(lpData, sarr.Length, 0);

                COPYDATASTRUCT cds = new()
                {
                    dwData = (IntPtr)1,
                    cbData = sarr.Length + 1,
                    lpData = lpData
                };

                return NativeMethods.SendMessage(_weChatHandle, WM_COPYDATA, wParam, ref cds);
            }
            finally
            {
                Marshal.FreeHGlobal(lpData);
            }
        }

        private void AppendLog(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), text);
                return;
            }

            if (txtLog.TextLength > 0)
            {
                txtLog.AppendText(Environment.NewLine);
            }

            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}");
        }

        /// <summary>
        /// 接收微信 API 通过 WM_COPYDATA 发来的消息
        /// </summary>
        /// <param name="m"></param>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_COPYDATA)
            {
                try
                {
                    object? data = m.GetLParam(typeof(COPYDATASTRUCT));
                    if (data is COPYDATASTRUCT cds)
                    {
                        // wParam 一般用来区分不同类型（例如 10003 登录信息、10012 消息等）
                        int wParam = m.WParam.ToInt32();

                        string? message = Marshal.PtrToStringAnsi(cds.lpData);

                        AppendLog($"收到 WM_COPYDATA，wParam={wParam}，内容：{message}");

                        if (!string.IsNullOrEmpty(message))
                        {
                            HandleWeChatMessage(wParam, message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"解析 WM_COPYDATA 消息异常：{ex.Message}");
                }
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// 按参考文章的约定解析微信 API 消息，并执行关键字自动回复
        /// </summary>
        private void HandleWeChatMessage(int wParam, string message)
        {
            switch (wParam)
            {
                case 10003: // 登录时解析用户信息
                    HandleLoginInfo(message);
                    break;

                case 10012: // 文本 / 图片 / 视频 / 文件等消息
                    HandleChatMessage(message);
                    break;

                default:
                    // 其它类型先简单忽略或仅日志
                    break;
            }
        }

        private void HandleLoginInfo(string message)
        {
            // 博客描述：登录时 message 用 "||" 分割，第一个是微信昵称，包含 "wxid_" 的是唯一标识，最后是头像 URL
            string[] parts = message.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);
            string nick = parts.Length > 0 ? parts[0] : "未知昵称";

            AppendLog($"登录成功，当前微信用户：{nick}");
        }

        private void HandleChatMessage(string message)
        {
            try
            {
                if (!message.Contains("msgtyp:", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string[] datas = Regex.Split(message, "msgtyp:", RegexOptions.IgnoreCase);
                if (datas.Length < 2)
                {
                    return;
                }

                string msgtyp = datas[1].Split(',')[0].Replace(" ", "");
                switch (msgtyp)
                {
                    case "1": // 文字消息
                        HandleTextMessage(message);
                        break;
                    default:
                        // 其它类型暂不处理
                        break;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"解析聊天消息失败：{ex.Message}");
            }
        }

        private void HandleTextMessage(string message)
        {
            // 参考文章里只对群文本消息做关键字匹配
            if (!message.Contains("[群-->]", StringComparison.OrdinalIgnoreCase) ||
                !message.Contains("[消息内容-->]", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string word = string.Empty;

            // 取消息内容（与博客 Regex.Split(message, @"消息内容-->]", ...) 一致）
            string[] wordArr = Regex.Split(message, @"消息内容-->]", RegexOptions.IgnoreCase);
            if (wordArr.Length >= 2)
            {
                string[] aa = wordArr[1].Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);
                if (aa.Length > 0)
                {
                    word = aa[0].StartsWith(" ") ? aa[0][1..] : aa[0];
                }
            }

            if (string.IsNullOrEmpty(word))
            {
                return;
            }

            string? chatRoomId = ExtractChatRoomId(message);

            AppendLog($"群文本消息：chatRoomId={chatRoomId ?? "未知"}，内容：{word}");

            if (chatRoomId is null)
            {
                return;
            }

            // 加载关键字配置，格式：关键字;回复内容
            var rules = ReadKeywordRules();
            foreach (var (key, reply) in rules)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(reply))
                {
                    continue;
                }

                if (word.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"检测到关键词 \"{key}\"，自动回复。");

                    string sendContent = $"{chatRoomId}||{reply}";
                    // 按参考文章，wParam=20001 表示发送文字消息
                    _ = SendWeChatMessage(sendContent, 20001);
                }
            }
        }

        /// <summary>
        /// 从 message 中提取群 chatroomId（@chatroom 结尾的字符串）
        /// </summary>
        private static string? ExtractChatRoomId(string message)
        {
            var match = Regex.Match(message, @"[0-9a-zA-Z_-]+@chatroom", RegexOptions.IgnoreCase);
            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// 读取关键字配置文件，默认在程序目录下 keywords.txt，每行格式：关键字;回复内容
        /// </summary>
        private static List<(string key, string reply)> ReadKeywordRules()
        {
            var result = new List<(string key, string reply)>();

            string filePath = Path.Combine(AppContext.BaseDirectory, "keywords.txt");
            if (!File.Exists(filePath))
            {
                return result;
            }

            foreach (string? line in File.ReadAllLines(filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(';');
                if (parts.Length < 2)
                {
                    continue;
                }

                string key = parts[0].Trim();
                string reply = string.Join(";", parts.Skip(1)).Trim();

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(reply))
                {
                    result.Add((key, reply));
                }
            }

            return result;
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            NativeMethods.UninstallHook();
        }
    }
}
