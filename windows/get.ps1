<#
    Codenotch bootstrap for Windows 11. Meant to be run as

        irm https://raw.githubusercontent.com/oltiir/codenotch-gnome/main/windows/get.ps1 | iex

    Piping into iex does not touch the execution policy, so this works on a
    stock Windows 11 (policy Restricted) with nothing to set up first. It
    downloads the latest release zip for this machine's architecture, verifies
    the published SHA-256, unpacks it into a temp directory and runs the
    install.ps1 inside -- through powershell.exe -ExecutionPolicy Bypass, so
    the policy cannot stop that either. The temp directory is removed again.

    Everything lives in one function that is called on the last line: half a
    download evaluated by iex therefore does nothing at all.

    Kept ASCII on purpose: Windows PowerShell 5.1 reads a BOM-less .ps1 as
    ANSI, so a stray em dash here would arrive mangled. Written for 5.1 --
    no ternaries, no null-coalescing, no && chaining.
#>

function Invoke-CodenotchBootstrap {
    $ErrorActionPreference = 'Stop'

    function Say  { param($m) Write-Host "==> " -ForegroundColor Cyan -NoNewline; Write-Host $m }
    function Ok   { param($m) Write-Host "  ok " -ForegroundColor Green -NoNewline; Write-Host $m }
    function Warn { param($m) Write-Host "  !  " -ForegroundColor Yellow -NoNewline; Write-Host $m }
    # throw, not exit: "irm ... | iex" runs in the caller's own session, and an
    # exit there would close the user's console before they could read why.
    function Die  { param($m) throw $m }

    if ($PSVersionTable.PSVersion.Major -lt 5) {
        Die "needs Windows PowerShell 5.1 or newer; this is $($PSVersionTable.PSVersion)"
    }

    Say "Preparing the download"
    # 5.1 defaults to SSL3/TLS1.0, which github.com refuses. Add TLS 1.2 rather
    # than replacing the set, so a shell that already has 1.3 keeps it.
    try {
        [Net.ServicePointManager]::SecurityProtocol =
            [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    } catch {
        Warn "could not force TLS 1.2 ($($_.Exception.Message))"
    }
    # Invoke-WebRequest's progress bar makes a 60 MB download several times slower
    # in 5.1; the byte count is not interesting here anyway.
    $ProgressPreference = 'SilentlyContinue'

    # Under WOW64 -- 32-bit PowerShell on an x64 or ARM64 Windows -- only
    # PROCESSOR_ARCHITEW6432 tells the truth about the machine.
    $machine = $env:PROCESSOR_ARCHITEW6432
    if ([string]::IsNullOrWhiteSpace($machine)) { $machine = $env:PROCESSOR_ARCHITECTURE }
    if ([string]::IsNullOrWhiteSpace($machine)) { $machine = 'AMD64' }
    $machine = $machine.ToUpper()

    $arch = ''
    if ($machine -eq 'ARM64') {
        $arch = 'arm64'
    } elseif ($machine -eq 'AMD64' -or $machine -eq 'IA64') {
        $arch = 'x64'
    } elseif ($machine -eq 'X86') {
        # 32-bit PowerShell with no ARCHITEW6432 set: either genuinely 32-bit
        # Windows, or an old shell on a 64-bit one. Ask the framework.
        if ([Environment]::Is64BitOperatingSystem) {
            $arch = 'x64'
            Warn "32-bit PowerShell on a 64-bit Windows; taking the x64 build"
        } else {
            Die "32-bit Windows is not supported; Codenotch ships x64 and ARM64 only"
        }
    } else {
        Die "unrecognised architecture '$machine'; expected AMD64 or ARM64"
    }
    Ok "$machine -> codenotch-windows-$arch.zip"

    $zipName = "codenotch-windows-$arch.zip"
    $base    = 'https://github.com/oltiir/codenotch-gnome/releases/latest/download'
    $temp    = Join-Path $env:TEMP ("codenotch-" + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    New-Item -ItemType Directory -Force -Path $temp | Out-Null

    try {
        $zip = Join-Path $temp $zipName
        $sum = "$zip.sha256"

        Say "Downloading the latest release"
        Invoke-WebRequest -Uri "$base/$zipName" -OutFile $zip -UseBasicParsing
        Invoke-WebRequest -Uri "$base/$zipName.sha256" -OutFile $sum -UseBasicParsing
        $size = [Math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
        Ok "$zipName, $size MB"

        Say "Verifying SHA-256"
        # The file CI writes is "<64 lowercase hex><two spaces><name>", no newline.
        $published = (Get-Content -LiteralPath $sum -Raw).Trim()
        $expected  = ($published -split '\s+')[0].ToLower()
        if ($expected -notmatch '^[0-9a-f]{64}$') {
            Die "the published .sha256 is not a SHA-256: '$published'"
        }
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower()
        if ($actual -ne $expected) {
            Warn "expected $expected"
            Warn "got      $actual"
            Die "checksum mismatch; the download is not the zip CI built. Nothing was installed."
        }
        Ok "$expected"

        Say "Unpacking"
        $unpacked = Join-Path $temp 'unpacked'
        Expand-Archive -LiteralPath $zip -DestinationPath $unpacked -Force
        $installer = Join-Path $unpacked 'install.ps1'
        if (-not (Test-Path -LiteralPath $installer)) {
            Die "install.ps1 is not in $zipName"
        }
        Ok "$unpacked"

        Say "Running the installer"
        Write-Host ""
        # -ExecutionPolicy Bypass on a fresh child process: the installer runs
        # whatever the machine policy is, and this pipeline never had to change it.
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
        $code = $LASTEXITCODE
        Write-Host ""
        if ($code -ne 0) {
            Die "install.ps1 exited with $code"
        }
    } finally {
        Say "Cleaning up"
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temp) {
            Warn "left $temp behind; delete it by hand"
        } else {
            Ok "removed $temp"
        }
    }
}

try {
    Invoke-CodenotchBootstrap
} catch {
    Write-Host "x  " -ForegroundColor Red -NoNewline
    Write-Host $_.Exception.Message
    $global:LASTEXITCODE = 1
}
