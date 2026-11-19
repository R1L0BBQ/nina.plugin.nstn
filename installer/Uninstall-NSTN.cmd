@echo off
rem Entfernt das NSTN-Plugin (vorher gesichert). Die NSTN-Einstellungen im N.I.N.A.-Profil bleiben.
setlocal
if not exist "%~dp0Install-NSTN.ps1" (
    echo Bitte das ZIP zuerst vollstaendig entpacken und Uninstall-NSTN.cmd aus dem entpackten Ordner starten.
    pause
    exit /b 1
)
rem Aus einem PowerShell-7-Fenster gestartet, erbt Windows PowerShell sonst dessen Modulpfad.
set "PSModulePath=%ProgramFiles%\WindowsPowerShell\Modules;%SystemRoot%\system32\WindowsPowerShell\v1.0\Modules"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-NSTN.ps1" -Uninstall %*
exit /b %errorlevel%
