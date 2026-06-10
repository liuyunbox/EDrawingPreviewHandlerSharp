@echo off
cd /d "%~dp0"

echo Unregistering COM server...
ServerRegistrationManager.exe uninstall EDrawingPreviewHandlerSharp.dll -codebase

echo Killing EDrawingViewerHost.exe...
taskkill /f /im EDrawingViewerHost.exe >nul 2>&1

echo Unregister complete!
echo You may need to restart explorer for changes to take effect.
pause