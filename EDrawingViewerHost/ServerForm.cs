using DuEDrawingControl;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EDrawingViewerHost
{
    /// <summary>
    /// 隐藏的服务窗体，承载 EDrawingView 控件，通过 WM_COPYDATA 接收命令
    /// 
    /// 注意：窗体不可最小化！最小化会导致 ActiveX 控件失去有效渲染区域。
    /// 改为屏幕外启动位置，保证 ActiveX 始终有有效窗口。
    /// </summary>
    public class ServerForm : Form
    {
        private EDrawingView _edrawingView;
        private System.Windows.Forms.Timer _idleTimer;
        private const int IdleTimeoutMs = 60000;

        // 自定义消息：WM_COPYDATA 的子类型
        private const int CMD_LOAD_FILE = 1;
        private const int CMD_HIDE = 2;
        private const int CMD_SHUTDOWN = 3;

        // eDrawing 窗口的句柄（ActiveX 子窗口），供 Handler 定位用
        internal static IntPtr DrawingHwnd = IntPtr.Zero;

        // 控件加载同步
        private bool _controlReady = false;
        private string _pendingFile = null;
        private IntPtr _pendingParent = IntPtr.Zero;

        // 内存管理
        private int _loadCount = 0;
        private const int MaxLoadsBeforeRestart = 30;
        private const long MemoryLimitMb = 500;
        private const long MemoryCheckInterval = 10;
        private int _memCheckCounter = 0;

        public ServerForm()
        {
            // 不最小化！最小化会使 eDrawing ActiveX 失去渲染表面
            // 改为屏幕外启动，保证 ActiveX 控件能正常初始化
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new System.Drawing.Point(-32000, -32000);
            this.Size = new System.Drawing.Size(800, 600);
            // 窗口标题供 Handler 查找
            this.Text = "EDrawingViewerSvr";
            Load += this.ServerForm_Load;
        }

        private void ServerForm_Load(object sender, EventArgs e)
        {
            // 顶部工具栏：测量 & 移动组件
            var toolStrip = new ToolStrip
            {
                Dock = DockStyle.Top,
                BackColor = System.Drawing.Color.FromArgb(45, 45, 48),
                ForeColor = System.Drawing.Color.White,
                AutoSize = false,
                Height = 32
            };

            var btnMeasure = new ToolStripButton("测量", null, (s, args) =>
            {
                try
                {
                    var markup = this._edrawingView.Markup;
                    if (markup == null)
                    {
                        Program.WriteLog("Measure: Markup unavailable");
                        return;
                    }
                    markup.ViewOperator_Set ( EMVMarkupOperators.eMVOperatorMeasure);
                    Program.WriteLog("Measure mode activated");
                }
                catch (Exception ex)
                {
                    Program.WriteLog("Measure error: " + ex.Message);
                }
            })
            {
                ForeColor = System.Drawing.Color.White,
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };

            var btnMove = new ToolStripButton("移动组件", null, (s, args) =>
            {
                try
                {
                    var markup = this._edrawingView.Markup;
                    if (markup == null)
                    {
                        Program.WriteLog("MoveComponent: Markup unavailable");
                        return;
                    }
                    markup.ViewOperator_Set(EMVMarkupOperators.eMVOperatorMoveComponent);
                    
                    Program.WriteLog("MoveComponent mode activated");
                }
                catch (Exception ex)
                {
                    Program.WriteLog("MoveComponent error: " + ex.Message);
                }
            })
            {
                ForeColor = System.Drawing.Color.White,
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };

            toolStrip.Items.Add(btnMeasure);
            toolStrip.Items.Add(btnMove);
            this.Controls.Add(toolStrip);

            // eDrawing 控件
            this._edrawingView = new EDrawingView { Dock = DockStyle.Fill };

            this._edrawingView.OnControlLoaded += ctrl =>
            {
                this._controlReady = true;
                Program.WriteLog("EDrawing control loaded");

                // 控件就绪后处理待加载的请求
                if (this._pendingFile != null)
                {
                    var file = this._pendingFile;
                    var parent = this._pendingParent;
                    this._pendingFile = null;
                    this._pendingParent = IntPtr.Zero;
                    Program.WriteLog("Processing deferred load: " + file);
                    this.DoLoadFile(file, parent);
                }
            };

            this.Controls.Add(this._edrawingView);

            // 空闲定时器：60 秒无请求自动退出
            this._idleTimer = new System.Windows.Forms.Timer { Interval = IdleTimeoutMs };
            this._idleTimer.Tick += (s, args) => { Program.WriteLog("Idle timeout, exiting"); Application.Exit(); };
            this._idleTimer.Start();
        }

        /// <summary>
        /// 处理 WM_COPYDATA 消息，接收 Handler 发送的命令
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_COPYDATA = 0x004A;

            if (m.Msg == WM_COPYDATA)
            {
                var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
                this.HandleCommand(cds, m.WParam);
                m.Result = (IntPtr)1; // 表示已处理
                return;
            }

            base.WndProc(ref m);
        }

        private void HandleCommand(COPYDATASTRUCT cds, IntPtr wParam)
        {
            // 重置空闲超时
            this._idleTimer?.Stop();
            this._idleTimer?.Start();

            switch (cds.dwData.ToInt32())
            {
                case CMD_LOAD_FILE:
                    this.OnLoadFile(cds, wParam);
                    break;
                case CMD_HIDE:
                    this.OnHide();
                    break;
                case CMD_SHUTDOWN:
                    Application.Exit();
                    break;
            }
        }

        /// <summary>
        /// 加载文件并嵌入到预览面板
        /// cds.lpData = "filePath\nparentHwnd"
        /// wParam = sender HWND（备用）
        /// </summary>
        private void OnLoadFile(COPYDATASTRUCT cds, IntPtr wParam)
        {
            try
            {
                var data = Marshal.PtrToStringUni(cds.lpData);
                if (string.IsNullOrEmpty(data)) return;

                var parts = data.Split('\n');
                var filePath = parts[0];
                var parentHwnd = parts.Length > 1 && long.TryParse(parts[1], out var h) ? (IntPtr)h : wParam;

                Program.WriteLog("LOAD_FILE: " + filePath + " parent=" + parentHwnd.ToInt64());

                if (!File.Exists(filePath))
                {
                    Program.WriteLog("File not found: " + filePath);
                    return;
                }

                // 如果控件还没就绪，缓存请求
                if (!this._controlReady)
                {
                    Program.WriteLog("Control not ready yet, deferring load request");
                    this._pendingFile = filePath;
                    this._pendingParent = parentHwnd;
                    return;
                }

                this.DoLoadFile(filePath, parentHwnd);
            }
            catch (Exception ex)
            {
                Program.WriteLog("OnLoadFile error: " + ex.Message);
            }
        }

  // 用于重试定位的变量
        private IntPtr _pendingChildHwnd = IntPtr.Zero;
        private System.Windows.Forms.Timer _retryPosTimer;

        /// <summary>
        /// 实际执行加载文件并设置父窗口（定位由重试定时器完成，等待预览面板就绪）
        /// </summary>
        private void DoLoadFile(string filePath, IntPtr parentHwnd)
        {
            try
            {
                // 停止之前的重制定时器
                this._retryPosTimer?.Stop();
                this._retryPosTimer?.Dispose();

                // 先清理旧文档释放资源
                this.CleanupDocument();

                Program.WriteLog("Opening file: " + filePath);

                // 打开文件（readOnly=true 避免锁定原始文件）
                if (this._edrawingView.EDrawingHost != null)
                {
                    this._edrawingView.EDrawingHost.FullUI = -1;
                    this._edrawingView.EDrawingHost.OpenDoc(filePath, false, false, true);
                    Program.WriteLog("File opened: " + filePath);
                }
                else
                {
                    Program.WriteLog("EDrawingHost is null, cannot open file");
                }

                // 建立所有权
                var myHwnd = this.Handle;
                SetWindowLong(myHwnd, GWL_HWNDPARENT, parentHwnd);

                // 查找 eDrawing 渲染子窗口
                var childHwnd = this.FindEDrawingChild();
                if (childHwnd != IntPtr.Zero)
                {
                    DrawingHwnd = childHwnd;
                    // 去掉标题栏等 chrome 样式
                    SetWindowLong(childHwnd, GWL_STYLE,
                        new IntPtr(GetWindowLong(childHwnd, GWL_STYLE) &
                            ~(WS_CAPTION | WS_BORDER | WS_DLGFRAME | WS_THICKFRAME)));
                }
                this._pendingChildHwnd = childHwnd;

                // 内存检查
                this._loadCount++;
                this._memCheckCounter++;
                if (this._memCheckCounter >= MemoryCheckInterval)
                {
                    this._memCheckCounter = 0;
                    this.CheckMemoryAndRestart();
                }

                // 启动重制定时器：因为预览面板在第二次及以后可能还没稳定布局
                // 需要反复尝试直到面板有非零大小
                this.StartRetryPosition(parentHwnd, childHwnd);
            }
            catch (Exception ex)
            {
                Program.WriteLog("DoLoadFile error: " + ex.Message);
            }
        }

        /// <summary>
        /// 清理文档资源，释放内存
        /// </summary>
        private void CleanupDocument()
        {
            if (this._edrawingView?.EDrawingHost != null)
            {
                try
                {
                    this._edrawingView.EDrawingHost.CloseActiveDoc();
                    Program.WriteLog("Document closed");
                }
                catch (Exception ex)
                {
                    Program.WriteLog("CloseActiveDoc error: " + ex.Message);
                }
            }
            // 强制 GC 释放 COM 包装对象（RCW）
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// <summary>
        /// 检查进程内存，超出限制则自我退出（Handler 会自动重启）
        /// </summary>
        private void CheckMemoryAndRestart()
        {
            try
            {
                using (var proc = Process.GetCurrentProcess())
                {
                    var mb = proc.PrivateMemorySize64 / (1024 * 1024);
                    Program.WriteLog("Memory check: " + mb + "MB (loads=" + this._loadCount + ")");

                    if (mb > MemoryLimitMb || this._loadCount >= MaxLoadsBeforeRestart)
                    {
                        var reason = mb > MemoryLimitMb
                            ? "memory exceeded " + MemoryLimitMb + "MB (current=" + mb + "MB)"
                            : "max loads reached (" + this._loadCount + ")";
                        Program.WriteLog("Self-exit: " + reason);
                        // 延迟一点退出，让当前预览完成
                        var exitTimer = new System.Windows.Forms.Timer();
                        exitTimer.Interval = 500;
                        exitTimer.Tick += (s, args) =>
                        {
                            exitTimer.Stop();
                            exitTimer.Dispose();
                            Application.Exit();
                        };
                        exitTimer.Start();
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 重试定位：预览面板可能还没完成布局，反复尝试直到面板大小有效
        /// </summary>
        private void StartRetryPosition(IntPtr parentHwnd, IntPtr childHwnd)
        {
            this._retryPosTimer = new System.Windows.Forms.Timer { Interval = 80 };
            this._retryPosTimer.Tick += (s, args) =>
            {
                var parentRect = new RECT();
                GetWindowRect(parentHwnd, out parentRect);
                var pw = parentRect.Right - parentRect.Left;
                var ph = parentRect.Bottom - parentRect.Top;

                // 面板还没就绪（大小为 0），继续等待下一次定时器
                if (pw <= 0 || ph <= 0) return;

                // 面板就绪，停止重试
                this._retryPosTimer.Stop();
                this._retryPosTimer.Dispose();

                var myHwnd = this.Handle;

                // 放置服务器窗口到预览面板位置
                SetWindowPos(myHwnd, IntPtr.Zero,
                    parentRect.Left, parentRect.Top, pw, ph,
                    SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);

                // 子渲染窗口铺满
                if (childHwnd != IntPtr.Zero)
                {
                    SetWindowPos(childHwnd, IntPtr.Zero,
                        0, 0, pw, ph,
                        SWP_NOACTIVATE | SWP_NOZORDER);
                }

                this.Invalidate(true);
                Program.WriteLog("Window positioned: " + pw + "x" + ph);
            };
            this._retryPosTimer.Start();
            Program.WriteLog("Position retry timer started");
        }

        private void OnHide()
        {
            Program.WriteLog("HIDE");
            this._retryPosTimer?.Stop();
            this._retryPosTimer?.Dispose();

            // 隐藏前关闭文档释放内存
            this.CleanupDocument();

            ShowWindow(this.Handle, SW_HIDE);
        }

        /// <summary>
        /// 获取或创建 Markup 控件（用于测量、标注等交互操作）
        /// 在隐藏/屏幕外窗口中，EDrawingView 初始化时 CoCreateInstance 可能失败，
        /// 所以需要在按钮点击时重新尝试创建
        /// </summary>
        private dynamic GetOrCreateMarkup()
        {
            // 优先使用 EDrawingView 已经创建的 Markup
            if (this._edrawingView?.Markup != null)
                return this._edrawingView.Markup;

            Program.WriteLog("GetOrCreateMarkup: _edrawingView.Markup is null, trying CoCreateInstance");

            if (this._edrawingView?.EDrawingHost == null)
            {
                Program.WriteLog("GetOrCreateMarkup: EDrawingHost is null");
                return null;
            }

            try
            {
                var raw = this._edrawingView.EDrawingHost.CoCreateInstance(
                    "EModelViewMarkup.EModelMarkupControl");
                if (raw == null)
                {
                    Program.WriteLog("GetOrCreateMarkup: CoCreateInstance returned null");
                    return null;
                }

                // 直接返回原始 COM 对象（dynamic），绕过 MarkupComponent 包装
                Program.WriteLog("GetOrCreateMarkup: success");
                return raw;
            }
            catch (Exception ex)
            {
                Program.WriteLog("GetOrCreateMarkup error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 查找 EDrawing ActiveX 控件的实际子窗口
        /// 遍历 EDrawingView 下的子控件，找到最底层的渲染窗口
        /// </summary>
        private IntPtr FindEDrawingChild()
        {
            // 遍历顶层子窗口，找 EDrawing 控件的渲染窗口
            var child = GetWindow(this.Handle, GW_CHILD);
            while (child != IntPtr.Zero)
            {
                var buf = new char[256];
                GetClassName(child, buf, 256);
                var cls = new string(buf).TrimEnd('\0');
                // eDrawing ActiveX 的类名通常包含 "EDrawing" 或属于 OLE 控件
                // 深度遍历，找到窗口尺寸最大的子窗口（就是渲染区域）
                var grandChild = GetWindow(child, GW_CHILD);
                while (grandChild != IntPtr.Zero)
                {
                    var buf2 = new char[256];
                    GetClassName(grandChild, buf2, 256);
                    var cls2 = new string(buf2).TrimEnd('\0');
                    Program.WriteLog("  child: " + cls2 + " HWND=" + grandChild.ToInt64());

                    // eDrawing 渲染窗口通常是可视面积最大的子窗口
                    var rc2 = new RECT();
                    GetWindowRect(grandChild, out rc2);
                    var area2 = (rc2.Right - rc2.Left) * (rc2.Bottom - rc2.Top);
                    if (area2 > 1000)
                    {
                        Program.WriteLog("  -> selected: " + cls2 + " area=" + area2);
                        return grandChild;
                    }
                    grandChild = GetWindow(grandChild, GW_HWNDNEXT);
                }
                child = GetWindow(child, GW_HWNDNEXT);
            }
            return IntPtr.Zero;
        }

        #region Win32 P/Invoke

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

        private const int GWL_HWNDPARENT = -8;
        private const int GWL_STYLE = -16;
        private const uint WS_CAPTION = 0x00C00000;
        private const uint WS_BORDER = 0x00800000;
        private const uint WS_DLGFRAME = 0x00400000;
        private const uint WS_THICKFRAME = 0x00040000;
        private const uint GW_CHILD = 5;
        private const uint GW_HWNDNEXT = 2;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;

        #endregion
    }
}