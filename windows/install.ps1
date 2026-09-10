#Requires -Version 5.1
<#
    Codenotch installer/uninstaller for Windows 11 -- per-user, no admin.
    Installs the published exe to %LOCALAPPDATA%\Programs\Codenotch, adds a
    Start menu shortcut and an HKCU Run key, and starts it. -Uninstall
    reverses all of that. Nothing outside %LOCALAPPDATA%, %APPDATA% and HKCU
    is ever touched.

    Kept ASCII on purpose: Windows PowerShell 5.1 reads a BOM-less .ps1 as
    ANSI, so a stray em dash or ellipsis here would arrive mangled.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Purge,
    [switch]$NoStart,
    [switch]$NoRunKey,
    [string]$Source = $PSScriptRoot,
    [string]$Destination = "$env:LOCALAPPDATA\Programs\Codenotch"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Say  { param($m) Write-Host "==> " -ForegroundColor Cyan -NoNewline; Write-Host $m }
function Ok   { param($m) Write-Host "  ok " -ForegroundColor Green -NoNewline; Write-Host $m }
function Warn { param($m) Write-Host "  !  " -ForegroundColor Yellow -NoNewline; Write-Host $m }
function Die  { param($m) Write-Host "x  " -ForegroundColor Red -NoNewline; Write-Host $m; exit 1 }

# $PSScriptRoot is empty when the script is piped into powershell rather than run
# as a file; Join-Path would then throw on an empty -Path.
if ([string]::IsNullOrWhiteSpace($Source)) { $Source = (Get-Location).ProviderPath }
if ([string]::IsNullOrWhiteSpace($Destination)) {
    Die "no -Destination and no LOCALAPPDATA to fall back on"
}

$RunKeyPath   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunValueName = 'Codenotch'
$ShortcutPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Codenotch.lnk"
$SettingsDir  = "$env:LOCALAPPDATA\Codenotch"
$SettingsPath = Join-Path $SettingsDir 'settings.json'
$Exe          = Join-Path $Destination 'Codenotch.exe'

function Stop-Codenotch {
    $running = Get-Process -Name Codenotch -ErrorAction SilentlyContinue
    if ($running) {
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        # The exe stays locked for a moment after the kill; without this wait the
        # Copy-Item below fails with "used by another process" on every reinstall.
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
        Ok "stopped"
    } else {
        Ok "nothing was running"
    }
}

function Remove-Quietly {
    param($Path, $What)
    if (-not (Test-Path -LiteralPath $Path)) {
        Ok "was not present"
        return
    }
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        Ok "removed"
    } catch {
        Warn "could not remove $What ($($_.Exception.Message))"
    }
}

function Install-Codenotch {
    if ($Purge) { Warn "-Purge only applies together with -Uninstall; ignoring it" }

    Say "Checking this machine"
    $build = [Environment]::OSVersion.Version.Build
    if ($build -lt 22000) {
        Warn "not Windows 11; Mica and rounded corners will be skipped"
    }
    Ok "$env:PROCESSOR_ARCHITECTURE, build $build"
    $srcExe = Join-Path $Source 'Codenotch.exe'
    if (-not (Test-Path -LiteralPath $srcExe)) {
        Die "Codenotch.exe not found in $Source"
    }

    Say "Stopping a running Codenotch"
    Stop-Codenotch

    Say "Installing to $Destination"
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    # Extracting the release zip straight into the destination and running the script
    # from there makes source and destination the same directory; Copy-Item would
    # throw "cannot be copied onto itself".
    $srcDir = (Resolve-Path -LiteralPath $Source).ProviderPath.TrimEnd('\')
    $dstDir = (Resolve-Path -LiteralPath $Destination).ProviderPath.TrimEnd('\')
    if ($srcDir -ieq $dstDir) {
        Ok "already in place"
    } else {
        Copy-Item -LiteralPath $srcExe -Destination $Destination -Force
        $readme = Join-Path $Source 'README.md'
        if (Test-Path -LiteralPath $readme) {
            Copy-Item -LiteralPath $readme -Destination $Destination -Force
        }
        Ok "$Exe"
    }
    # Clears the mark-of-the-web zone identifier so SmartScreen does not
    # re-prompt on every launch.
    Unblock-File -LiteralPath $Exe -ErrorAction SilentlyContinue

    Say "Registering the Start menu shortcut"
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ShortcutPath) | Out-Null
        $wshShell = New-Object -ComObject WScript.Shell
        try {
            $shortcut = $wshShell.CreateShortcut($ShortcutPath)
            $shortcut.TargetPath = $Exe
            $shortcut.WorkingDirectory = $Destination
            $shortcut.Description = 'Codenotch'
            $shortcut.Save()
        } finally {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($wshShell)
        }
        Ok "$ShortcutPath"
    } catch {
        # A shortcut is a convenience; a locked-down COM policy should not fail the install.
        Warn "could not create the shortcut ($($_.Exception.Message))"
    }

    if ($NoRunKey) {
        Warn "skipping the Run key (-NoRunKey)"
    } else {
        Say "Starting with Windows"
        # Quoted, and REG_SZ, so it matches byte for byte what the app's own
        # "Start with Windows" toggle writes (StartupRegistration.Enable).
        Set-ItemProperty -Path $RunKeyPath -Name $RunValueName -Value "`"$Exe`"" -Type String
        Ok "Run key set"
    }

    Say "Checking your providers"
    $haveClaude = Test-Path -LiteralPath "$env:USERPROFILE\.claude\.credentials.json"
    $haveCodex  = Test-Path -LiteralPath "$env:USERPROFILE\.codex\auth.json"
    if ($haveClaude) { Ok "claude signed in" } else { Warn "claude not signed in here -- run 'claude' once" }
    if ($haveCodex)  { Ok "codex signed in" }  else { Warn "codex not signed in here -- run 'codex login'" }
    if (-not $haveClaude -and -not $haveCodex) {
        Warn "no providers signed in; the tray dial will stay grey and empty"
    }

    if ($NoStart) {
        Warn "not starting Codenotch (-NoStart)"
    } else {
        Say "Starting Codenotch"
        Start-Process -FilePath $Exe
        Ok "running in the notification area"
    }

    @"

  Done. Codenotch is running in the notification area.

  Settings live at $SettingsPath and are also editable from the tray menu's
  Settings item. To uninstall later:
    .\install.ps1 -Uninstall
"@ | Write-Host
}

function Uninstall-Codenotch {
    Say "Stopping a running Codenotch"
    Stop-Codenotch

    Say "Removing the Run key"
    Remove-ItemProperty -Path $RunKeyPath -Name $RunValueName -ErrorAction SilentlyContinue
    Ok "removed (or was not set)"

    Say "Removing the Start menu shortcut"
    Remove-Quietly -Path $ShortcutPath -What 'the shortcut'

    Say "Removing $Destination"
    Remove-Quietly -Path $Destination -What $Destination

    if ($Purge) {
        Say "Removing settings ($SettingsDir)"
        Remove-Quietly -Path $SettingsDir -What $SettingsDir
    } else {
        Warn "keeping $SettingsPath (pass -Purge to remove it too)"
    }

    Write-Host ""
    Write-Host "  Done. Codenotch is uninstalled."
}

if ($Uninstall) {
    Uninstall-Codenotch
} else {
    Install-Codenotch
}
