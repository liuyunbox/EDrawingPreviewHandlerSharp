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

echo Adding preview handler registrations...
set CLSID={58E3E5DE-76BC-4935-ACA1-B4027F00C696}
set PREVIEW_KEY={8895b1c6-b41f-4c1c-a562-0d564250836f}

for %%e in (.sldprt .sldasm .slddrw .easm .eprt .edrw .igs .iges .step .stp .x_t .x_b .dwfx .dxf .dwg .stl .tif .tiff) do (
    reg add "HKCR\%%e\ShellEx\%PREVIEW_KEY%" /ve /t REG_SZ /d "%CLSID%" /f >nul 2>&1
    if !errorLevel! equ 0 (
        echo   [OK] %%e
    ) else (
        echo   [FAILED] %%e
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