@echo off
rem Startet den NSTN-Installer mit dem Windows PowerShell, das jedes Windows mitbringt.
rem Weitere Argumente werden durchgereicht, z. B. -Uninstall oder -PluginDir "D:\NINA\Plugins\3.0.0\NSTN".
setlocal
if not exist "%~dp0Install-NSTN.ps1" (
    echo Bitte das ZIP zuerst vollstaendig entpacken und Install-NSTN.cmd aus dem entpackten Ordner starten.
    pause
    exit /b 1
)
rem Aus einem PowerShell-7-Fenster gestartet, erbt Windows PowerShell sonst dessen Modulpfad.
set "PSModulePath=%ProgramFiles%\WindowsPowerShell\Modules;%SystemRoot%\system32\WindowsPowerShell\v1.0\Modules"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-NSTN.ps1" %*
exit /b %errorlevel%
