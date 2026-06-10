using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpShell.Attributes;
using SharpShell.SharpPreviewHandler;

namespace EDrawingPreviewHandlerSharp
{
    /// <summary>
    /// eDrawing 文件预览处理器 —— SharpShell COM 组件
    /// 
    /// 工作原理：
    /// 1. 常驻 EDrawingViewerHost.exe 进程在后台运行，承载 eDrawing ActiveX
    /// 2. 预览时发送 WM_COPYDATA 消息通知 EXE 加载文件
    /// 3. EXE 通过 GWL_HWNDPARENT 建立所有权，将窗口定位到预览面板
    /// 4. 因 EXE 常驻，预览切换无需重复启动，几乎无空白等待
    /// </summary>
    [ComVisible(true)]
    [Guid("58E3E5DE-76BC-4935-ACA1-B4027F00C696")]
    [DisplayName("EDrawing Preview Handler")]
    [PreviewHandler(DisableLowILProcessIsolation = true)]
    [COMServerAssociation(AssociationType.FileExtension, ".easm")]
    [COMServerAssociation(AssociationType.FileExtension, ".eprt")]
    [COMServerAssociation(AssociationType.FileExtension, ".edrw")]
    [COMServerAssociation(AssociationType.FileExtension, ".sldprt")]
    [COMServerAssociation(AssociationType.FileExtension, ".sldasm")]
    [COMServerAssociation(AssociationType.FileExtension, ".slddrw")]
    [COMServerAssociation(AssociationType.FileExtension, ".igs")]
    [COMServerAssociation(AssociationType.FileExtension, ".iges")]
    [COMServerAssociation(AssociationType.FileExtension, ".step")]
    [COMServerAssociation(AssociationType.FileExtension, ".stp")]
    [COMServerAssociation(AssociationType.FileExtension, ".x_t")]
    [COMServerAssociation(AssociationType.FileExtension, ".x_b")]
    [COMServerAssociation(AssociationType.FileExtension, ".dwfx")]
    [COMServerAssociation(AssociationType.FileExtension, ".dxf")]
    [COMServerAssociation(AssociationType.FileExtension, ".dwg")]
    [COMServerAssociation(AssociationType.FileExtension, ".stl")]
    [COMServerAssociation(AssociationType.FileExtension, ".tif")]
    [COMServerAssociation(AssociationType.FileExtension, ".tiff")]
    public class EDrawingPreviewHandler : SharpPreviewHandler
    {
        protected override PreviewHandlerControl DoPreview()
        {
            var filePath = this.SelectedFilePath;
            var dllDir = Path.GetDirectoryName(typeof(EDrawingPreviewHandler).Assembly.Location) ?? ".";
            var exePath = Path.Combine(dllDir, "EDrawingViewerHost.exe");
            WriteLog("DoPreview. File: " + (filePath ?? "(null)"));
            return new EDrawingPreviewControl(filePath, exePath);
        }

        internal static void WriteLog(string message)
        {
            try
            {
                var logPath = Path.Combine(
                    Path.GetDirectoryName(typeof(EDrawingPreviewHandler).Assembly.Location) ?? ".",
                    "EDrawingPreview_Log.txt");
                File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss") + " " + message + "\n");
            }
            catch { }
        }
    }
}