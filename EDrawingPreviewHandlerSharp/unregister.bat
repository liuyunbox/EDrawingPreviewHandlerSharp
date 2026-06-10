@echo off
cd /d "%~dp0"

echo Unregistering COM server...
ServerRegistrationManager.exe uninstall EDrawingPreviewHandlerSharp.dll -codebase

echo Removing preview handler registrations...
set PREVIEW_KEY={8895b1c6-b41f-4c1c-a562-0d564250836f}

:: 清理扩展名路径
for %%e in (.sldprt .sldasm .slddrw .easm .eprt .edrw .igs .iges .step .stp .x_t .x_b .dwfx .dxf .dwg .stl .tif .tiff) do (
    reg delete "HKCR\%%e\ShellEx\%PREVIEW_KEY%" /ve /f >nul 2>&1
)

:: 清理 ProgID 路径（SolidWorks）
for %%p in (SldAssem.Document SldPart.Document SldDraw.Document) do (
    reg delete "HKCR\%%p\ShellEx\%PREVIEW_KEY%" /ve /f >nul 2>&1
)

echo Killing EDrawingViewerHost.exe...
taskkill /f /im EDrawingViewerHost.exe >nul 2>&1

echo Unregister complete!
echo You may need to restart explorer for changes to take effect.
pause