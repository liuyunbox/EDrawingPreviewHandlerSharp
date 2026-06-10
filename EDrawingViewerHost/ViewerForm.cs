using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DuEDrawingControl;

namespace EDrawingViewerHost
{
    /// <summary>
    /// eDrawing 预览宿主窗口
    /// 
    /// 独立 WinForms 窗口，承载 eDrawing ActiveX 控件。
    /// 由 EDrawingPreviewHandler 启动，通过 FindWindow(窗口标题) 找到后
    /// 使用 SetParent 嵌入预览面板。
    /// </summary>
    public class ViewerForm : Form
    {
        private readonly string _filePath;

        public ViewerForm(string filePath)
        {
            this._filePath = filePath;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new System.Drawing.Point(-32000, -32000); // offscreen
            this.Size = new System.Drawing.Size(800, 600);
            this.Text = $"EDrawingViewer_{Process.GetCurrentProcess().Id}";

            Load += this.ViewerForm_Load;
        }

        private void ViewerForm_Load(object sender, EventArgs e)
        {
            var edrawingView = new EDrawingView { Dock = DockStyle.Fill };

            edrawingView.OnControlLoaded += ctrl =>
            {
                try
                {
                    WriteLog("EDrawing control loaded");

                    if (!string.IsNullOrEmpty(this._filePath) && File.Exists(this._filePath))
                    {
                        edrawingView.EDrawingHost.FullUI = 0;
                        edrawingView.EDrawingHost.OpenDoc(this._filePath, false, false, false);
                        WriteLog("File opened: " + this._filePath);
                    }
                    else
                    {
                        WriteLog("File not found: " + (this._filePath ?? "(null)"));
                    }
                }
                catch (Exception ex)
                {
                    WriteLog("Error: " + ex.Message);
                }
            };

            this.Controls.Add(edrawingView);
        }

        internal static void WriteLog(string message)
        {
            try
            {
                var logPath = Path.Combine(
                    Path.GetDirectoryName(typeof(ViewerForm).Assembly.Location) ?? ".",
                    "EDrawingViewer_Log.txt"
                );
                File.AppendAllText(logPath,
                    DateTime.Now.ToString("HH:mm:ss") + " [ViewerForm] " + message + "\n");
            }
            catch { }
        }
    }
}