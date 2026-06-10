using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpShell.SharpPreviewHandler;

namespace EDrawingPreviewHandlerSharp
{
    /// <summary>
    /// 嵌入预览面板的 UserControl
    /// 利用常驻 EXE 进程加载文件，通过 WM_COPYDATA 通信
    /// </summary>
    public class EDrawingPreviewControl : PreviewHandlerControl
    {
        private readonly string _filePath;
        private readonly string _exePath;
        private Timer _embedTimer;
        private IntPtr _svrHwnd = IntPtr.Zero;
        private Label _loadingLabel;
        private int _retryCount = 0;
        private const int MaxRetries = 80; // 最多等 80*50ms = 4 秒

        // 静态：常驻进程在多次预览间复用
        private static Process _serverProcess;
        private const string ServerWindowTitle = "EDrawingViewerSvr";

        public EDrawingPreviewControl(string filePath, string exePath)
        {
            this._filePath = filePath;
            this._exePath = exePath;

            this._loadingLabel = new Label
            {
                Text = "正在加载...",
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.Gray,
                Font = new System.Drawing.Font("Microsoft YaHei", 12f)
            };
            this.Controls.Add(this._loadingLabel);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!File.Exists(this._exePath)) { EDrawingPreviewHandler.WriteLog("EXE not found: " + this._exePath); return; }
            if (string.IsNullOrEmpty(this._filePath) || !File.Exists(this._filePath))
            { EDrawingPreviewHandler.WriteLog("File not found: " + (this._filePath ?? "(null)")); return; }

            try
            {
                // 确保常驻进程在运行
                this.EnsureServerRunning();

                // 发消息给 EXE 加载文件
                this._svrHwnd = FindWindow(null, ServerWindowTitle);
                if (this._svrHwnd == IntPtr.Zero)
                {
                    EDrawingPreviewHandler.WriteLog("Server window not found");
                    return;
                }

                this.SendLoadFileCommand(this._svrHwnd, this._filePath);

                // 启动定时器等窗口定位完成（EXE 加载后自行定位）
                this._embedTimer = new Timer { Interval = 50 };
                this._embedTimer.Tick += this.OnEmbedTimerTick;
                this._embedTimer.Start();
            }
            catch (Exception ex) { EDrawingPreviewHandler.WriteLog("Error: " + ex.Message); }
        }

        /// <summary>
        /// 确保常驻 EXE 进程运行
        /// </summary>
        private void EnsureServerRunning()
        {
            // 检查已缓存的进程是否还活着
            if (_serverProcess != null && !_serverProcess.HasExited)
                return;

            // 按窗口标题找（可能进程缓存失效但窗口还在）
            var existingWnd = FindWindow(null, ServerWindowTitle);
            if (existingWnd != IntPtr.Zero)
            {
                EDrawingPreviewHandler.WriteLog("Found existing server window");
                return;
            }

            // 启动新进程
            EDrawingPreviewHandler.WriteLog("Starting server: " + this._exePath);
            var psi = new ProcessStartInfo(this._exePath)
            {
                UseShellExecute = true,   // UseShellExecute=true 可以启动 WinForms 应用
                WindowStyle = ProcessWindowStyle.Hidden
            };
            _serverProcess = Process.Start(psi);
            EDrawingPreviewHandler.WriteLog("Server started PID=" + _serverProcess.Id);

            // 等待窗口创建（首次启动可能稍慢）
            for (var i = 0; i < 50; i++)
            {
                System.Threading.Thread.Sleep(40);
                var wnd = FindWindow(null, ServerWindowTitle);
                if (wnd != IntPtr.Zero)
                {
                    EDrawingPreviewHandler.WriteLog("Server window found after " + (i * 40) + "ms");
                    return;
                }
            }
            EDrawingPreviewHandler.WriteLog("Server window not found after startup");
        }

        /// <summary>
        /// 发送 WM_COPYDATA 加载文件命令
        /// 数据格式：文件路径 + "\n" + 父窗口句柄
        /// </summary>
        private void SendLoadFileCommand(IntPtr svrHwnd, string filePath)
        {
            var data = filePath + "\n" + this.Handle.ToInt64() + "\0";
            var bytes = System.Text.Encoding.Unicode.GetBytes(data);

            var cds = new COPYDATASTRUCT
            {
                dwData = (IntPtr)1, // CMD_LOAD_FILE
                cbData = bytes.Length,
                lpData = Marshal.AllocCoTaskMem(bytes.Length)
            };
            Marshal.Copy(bytes, 0, cds.lpData, bytes.Length);

            try
            {
                SendMessage(svrHwnd, WM_COPYDATA, this.Handle, ref cds);
                EDrawingPreviewHandler.WriteLog("Load command sent: " + filePath);
            }
            finally
            {
                Marshal.FreeCoTaskMem(cds.lpData);
            }
        }

        private void OnEmbedTimerTick(object sender, EventArgs e)
        {
            this._retryCount++;
            if (this._retryCount > MaxRetries)
            {
                this._embedTimer.Stop();
                EDrawingPreviewHandler.WriteLog("Embed timeout, giving up");
                if (this._loadingLabel != null)
                {
                    this._loadingLabel.Text = "预览加载超时";
                    this._loadingLabel.ForeColor = System.Drawing.Color.Red;
                }
                return;
            }

            // 使用已缓存的 _svrHwnd 而不是反复 FindWindow
            if (this._svrHwnd == IntPtr.Zero || !IsWindowVisible(this._svrHwnd))
                return;

            this._embedTimer.Stop();
            EDrawingPreviewHandler.WriteLog("Server window is visible, ready (retry=" + this._retryCount + ")");

            if (this._loadingLabel != null)
                this._loadingLabel.Visible = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // EXE 自行定位，不需要这边干预
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this._embedTimer?.Stop();
                this._embedTimer?.Dispose();

                // 发 HIDE 命令，但不杀进程（常驻）
                if (this._svrHwnd != IntPtr.Zero)
                {
                    try
                    {
                        var cds = new COPYDATASTRUCT
                        {
                            dwData = (IntPtr)2, // CMD_HIDE
                            cbData = 0,
                            lpData = IntPtr.Zero
                        };
                        SendMessage(this._svrHwnd, WM_COPYDATA, IntPtr.Zero, ref cds);
                    }
                    catch { }
                }
            }
            base.Dispose(disposing);
        }

        #region Win32 P/Invoke

        private const int WM_COPYDATA = 0x004A;

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        #endregion
    }
}