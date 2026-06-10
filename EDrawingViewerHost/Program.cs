using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using DuEDrawingControl;

namespace EDrawingViewerHost
{
    /// <summary>
    /// eDrawing 常驻服务 —— 后台进程，通过窗口消息接收预览请求
    /// 
    /// 与 EDrawingPreviewHandler 配合工作：
    /// - Handler 通过 FindWindow("EDrawingViewerSvr") 找到本窗口
    /// - 发送 WM_COPYDATA 消息加载文件
    /// - 本窗口加载后通过 GWL_HWNDPARENT 建立所有权，定位到预览区域
    /// - 空闲 60 秒后自动退出
    /// </summary>
    internal static class Program
    {
        internal static string LogPath = Path.Combine(
            Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".", "EDrawingViewer_Log.txt");

        [STAThread]
        static void Main()
        {
            // 确保单例
            using (new Mutex(true, "EDrawingViewerHost_Singleton", out var createdNew))
            {
                if (!createdNew) return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                WriteLog("Server started");
                Application.Run(new ServerForm());
            }
            WriteLog("Server exited");
        }

        internal static void WriteLog(string msg)
        {
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " [Svr] " + msg + "\n"); }
            catch { }
        }
    }
}