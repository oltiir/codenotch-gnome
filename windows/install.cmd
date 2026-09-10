@echo off
rem Double-clickable wrapper around install.ps1. -ExecutionPolicy Bypass on a
rem fresh powershell.exe, so a stock Windows 11 (policy Restricted) runs it as
rem it comes. Any arguments are passed straight through:
rem
rem     install.cmd -Uninstall
rem     install.cmd -NoStart -NoRunKey
setlocal EnableExtensions
set "PSFILE=%~dp0install.ps1"
if not exist "%PSFILE%" (
    echo x  install.ps1 is not next to install.cmd -- extract the whole zip.
    set "RC=1"
    goto :after
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PSFILE%" %*
set "RC=%ERRORLEVEL%"
:after
rem Explorer runs a double-clicked .cmd as "cmd /c ...", and that window closes
rem the moment we return, so wait for a keypress there. A shell that was already
rem open has a prompt to come back to, and anyone passing arguments is scripting
rem it -- neither wants to be nagged.
if not "%~1"=="" goto :done
echo %cmdcmdline% | find /i "/c" >nul
if not errorlevel 1 pause
:done
exit /b %RC%
