@echo off
cd /d "%~dp0"

title EDrawing Preview Handler Register

echo Registering COM server...
ServerRegistrationManager.exe install EDrawingPreviewHandlerSharp.dll -codebase
if %errorLevel% neq 0 (
    echo [ERROR] ServerRegistrationManager failed
    pause
    exit /b 1
)

echo.
echo Adding preview handler registrations...

:: 从 DLL 中自动提取 GUID，与代码中的 [Guid("...")] 保持同步
for /f "delims=" %%a in ('powershell -NoProfile -Command "& { Add-Type -AssemblyName System.Runtime.InteropServices; $asm = [System.Reflection.Assembly]::LoadFrom('%~dp0EDrawingPreviewHandlerSharp.dll'); $attr = $asm.GetType('EDrawingPreviewHandlerSharp.EDrawingPreviewHandler').GetCustomAttributes([System.Runtime.InteropServices.GuidAttribute], $false); if ($attr -and $attr.Length -gt 0) { '{' + $attr[0].Value.ToUpper() + '}' } else { Write-Error 'GUID not found'; exit 1 } }"') do set CLSID=%%a

if "%CLSID%"=="" (
    echo [ERROR] Failed to extract GUID from DLL
    pause
    exit /b 1
)

echo   CLSID=%CLSID%
set PREVIEW_KEY={8895b1c6-b41f-4c1c-a562-0d564250836f}

:: 1) 按扩展名注册预览 handler（标准路径）
for %%e in (.sldprt .sldasm .slddrw .easm .eprt .edrw .igs .iges .step .stp .x_t .x_b .dwfx .dxf .dwg .stl .tif .tiff) do (
    reg add "HKCR\%%e\ShellEx\%PREVIEW_KEY%" /ve /t REG_SZ /d "%CLSID%" /f >nul 2>&1
    if !errorLevel! equ 0 (
        echo   [OK] %%e
    ) else (
        echo   [FAILED] %%e
    )
)

:: 2) 按 ProgID 注册预览 handler（覆盖 SolidWorks 注册的旧 handler）
echo.
echo Adding ProgID-level registrations (override SolidWorks)...
for %%p in (SldAssem.Document SldPart.Document SldDraw.Document) do (
    reg add "HKCR\%%p\ShellEx\%PREVIEW_KEY%" /ve /t REG_SZ /d "%CLSID%" /f >nul 2>&1
    if !errorLevel! equ 0 (
        echo   [OK] %%p
    ) else (
        echo   [FAILED] %%p
    )
)

echo.
echo Register success!

echo Restarting explorer...
taskkill /f /im explorer.exe >nul 2>&1
start explorer.exe

echo.
echo Done! Press Alt+P to test preview in file explorer.
pause