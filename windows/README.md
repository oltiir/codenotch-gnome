# Codenotch for Windows 11

Claude Code and Codex usage limits in the Windows tray. This is the Windows
counterpart of the GNOME extension one directory up: the same concentric
dials, the same claude.ai-style details, the same 70/90 colours.

```
tray:    ◔  68%              ← the dial, or the percent as tabular digits

flyout:  (✱)  Claude
              Current session    ████████░░░░   68% used
              Resets in 59 min  ·  17:10
              Weekly limits
              All models         █████░░░░░░░   47% used
              Fable              ███████░░░░░   64% used
              27% in reserve | Expected 39% used | Lasts until reset
              Updated 12 s ago                    Refresh   ⚙

notch:   ╭──────╮   flush to the right edge, vertically centred
         │ (✱)  │   outer ring = current session, inner = weekly
         │ 68%  │   hover → the callout slides out to the left
         │wk 47%│   click pins it, right-click opens the tray menu
         ╰──────╯
```

Everything is percent **used**, the way claude.ai reports it. Green under
70%, yellow from 70%, red from 90%.

## Install

In any PowerShell window:

```powershell
irm https://raw.githubusercontent.com/oltiir/codenotch-gnome/main/windows/get.ps1 | iex
```

One line, and nothing to set up first: a pipeline into `iex` is not a script
file, so the execution policy — `Restricted` on a stock Windows 11 — does not
apply to it. `get.ps1` works out whether this machine is x64 or ARM64,
downloads that zip from the latest
[release](https://github.com/oltiir/codenotch-gnome/releases/latest), checks it
against the published `.sha256`, unpacks it into a temp directory, runs the
`install.ps1` inside through `powershell -ExecutionPolicy Bypass`, and deletes
the temp directory again.

The install is per-user: no admin, and no .NET needed on the machine. It puts
`Codenotch.exe` in `%LOCALAPPDATA%\Programs\Codenotch` — with copies of
`install.ps1` and `install.cmd` beside it, so uninstalling later needs nothing
you have to keep — adds a Start menu shortcut and an HKCU `Run` key, and starts
it. It shows up as a dial in the notification area beside the clock; Windows
hides new tray icons behind the `^` arrow until you drag them out. Claude Code
and/or Codex have to be signed in on this machine already — that's where the
numbers come from.

Prefer to see what you run? Download `codenotch-windows-x64.zip` (ARM64
laptops: `codenotch-windows-arm64.zip`) from the
[release page](https://github.com/oltiir/codenotch-gnome/releases/latest),
extract it, and double-click **`install.cmd`** — it runs the same `install.ps1`
with `-ExecutionPolicy Bypass` and waits for a keypress at the end, so the
window does not vanish before you have read it. The exe is portable, too:
running `Codenotch.exe` out of the extracted folder tries the app with no
install, no shortcut and no autostart.

The exe is unsigned, so SmartScreen may say **Windows protected your PC** the
first time it runs: choose *More info → Run anyway*. The `.sha256` next to the
zip on the release page is how to check the download before that; the one-liner
checks it for you.

### Verifying the download by hand

```powershell
cd ~\Downloads
irm https://github.com/oltiir/codenotch-gnome/releases/latest/download/codenotch-windows-x64.zip -OutFile codenotch-windows-x64.zip
irm https://github.com/oltiir/codenotch-gnome/releases/latest/download/codenotch-windows-x64.zip.sha256 -OutFile codenotch-windows-x64.zip.sha256
(Get-FileHash codenotch-windows-x64.zip -Algorithm SHA256).Hash.ToLower() -eq (Get-Content codenotch-windows-x64.zip.sha256).Split(' ')[0]
Expand-Archive codenotch-windows-x64.zip -DestinationPath codenotch
.\codenotch\install.cmd
```

`True` from the fourth line means the zip is the one CI built. That is the same
comparison `get.ps1` makes; the published file is the hash, two spaces and the
file name, with no trailing newline.

## Build from source

Needs the .NET 10 SDK.

```powershell
dotnet test windows\tests\Codenotch.Core.Tests
dotnet publish windows\src\Codenotch.App -c Release -r win-x64
.\windows\install.cmd -Source windows\src\Codenotch.App\bin\Release\net10.0-windows\win-x64\publish
```

## Did it work?

The tray dial appears within a few seconds of starting Codenotch. A grey
dial with no filled arc means it has no reading (in `trayPercent` mode the
same state shows as `—`, and the notch swaps its dials for one grey `!`
above the word `offline`) -- a data problem, not an app problem:

```powershell
claude auth status      # is Claude Code signed in?
codex login              # is Codex signed in?
curl http://127.0.0.1:8787/usage   # only relevant in endpoint mode
```

Errors also land in `%LOCALAPPDATA%\Codenotch\error.log`.

## Configuration

Settings live at `%LOCALAPPDATA%\Codenotch\settings.json` and are also
editable from the tray menu's *Settings…* item. Right-clicking the tray icon
(or the notch) gives *Refresh now*, *Show edge notch*, *Show percent in
tray*, *Start with Windows*, *Settings…* and *Quit*; left-clicking the icon
toggles the flyout. *Start with Windows* is the HKCU `Run` key and
deliberately not in the JSON, so a copied settings file cannot claim the app
autostarts when it does not.

| Setting (`settings.json`) | Default | Notes |
| --- | --- | --- |
| `pollSeconds` | `60` | 30 in endpoint mode, but only until something writes the file: the Settings window always saves an explicit value. Clamped to 10–3600. Each built-in poll hits the vendor directly |
| `endpoint` | `null` | e.g. `http://127.0.0.1:8787/usage` — reads a `codexbar serve` instead of the built-in providers (WSL parity with the Linux ports) |
| `showNotch` | `true` | `false` leaves only the tray icon |
| `trayPercent` | `false` | `true` swaps the dial for the session percent |
| `notify` | `true` | Balloon toasts when a window crosses 70% and 90% |
| `providers` | `null` | `null` = automatic (whichever credential files exist); or `["claude"]` |

Thresholds are `WarnAt = 70` / `CriticalAt = 90` in
`Codenotch.Core/Presentation/Tone.cs`. Ring sizes and paddings live in
`Codenotch.App/NotchWindow.xaml`.

## Privacy

Unlike the GNOME, COSMIC and waybar ports, which only read a localhost
server and never touch credentials, the Windows app **reads the token files
Claude Code and Codex already wrote**
(`%USERPROFILE%\.claude\.credentials.json`,
`%USERPROFILE%\.codex\auth.json`) because CodexBar ships no Windows CLI.
Those tokens are sent only to `api.anthropic.com` and `chatgpt.com` over
HTTPS, exactly as the vendors' own tools do. Nothing is stored, nothing is
logged, no telemetry, no third-party endpoint. It never writes the
credential files; when a Claude token has expired it asks the `claude` CLI
to refresh it rather than exchanging the refresh token itself.

Set `endpoint` if you would rather it never read a credential file at all —
it then reads a local `codexbar serve` instead, same as the other ports.

## Uninstall

From `%LOCALAPPDATA%\Programs\Codenotch` (where the installer left both
scripts), or from the extracted zip:

```powershell
.\install.cmd -Uninstall
```

Or, to see the script rather than the wrapper:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
```

Or by hand: remove the `Codenotch` value from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, delete
`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Codenotch.lnk`, then
delete `%LOCALAPPDATA%\Programs\Codenotch` and `%LOCALAPPDATA%\Codenotch`.
`-Uninstall` keeps `settings.json` unless you add `-Purge`.

## Troubleshooting

**"Windows protected your PC"** — the exe is unsigned, so SmartScreen warns
on first run: *More info → Run anyway*, or `Unblock-File .\Codenotch.exe`
(which `install.ps1` already does). Verify the `.sha256` from the release
first.

**"install.ps1 cannot be loaded because running scripts is disabled on this
system"** — Windows 11's execution policy is `Restricted` out of the box, so
a bare `.\install.ps1` is exactly the thing that fails. Both documented paths
avoid it: `irm ... | iex` is a pipeline rather than a script file, and
`install.cmd` starts `powershell.exe -ExecutionPolicy Bypass`. To run the
script directly anyway, do the same by hand:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

There is no need to change the machine's policy with `Set-ExecutionPolicy`.

**"Claude token expired; run `claude` once to refresh"** — the app
deliberately does not rotate your refresh token (that would race Claude
Code's own writes). Run `claude` (or `claude auth status`) once in a
terminal; the next poll picks the new token up. At most one delegated
refresh is attempted every 5 minutes.

**"unauthorized, run `claude` to sign in again"** / **`codex login`** — a
401; the credential file is stale or from another account.

**"rate limited"** — a 429 gates further calls for 5 minutes (or until
`Retry-After`); *Refresh now* does not bypass it, by design.

**No tray icon** — check Settings → Personalization → Taskbar → *Other
system tray icons*; also check for a second instance (the named mutex
silently exits duplicates).

**The notch is in the way** — turn it off in the tray menu, or set
`"showNotch": false`.

**Blurry or misplaced notch on a mixed-DPI setup** — it re-places on
display-settings and DPI changes; if it drifts, toggle *Show edge notch*.

**No Mica / square corners** — Windows 10 or an old Windows 11 build; the
DWM attributes are optional and the app falls back to solid brushes. (The
flyout asks for acrylic, the Settings window for Mica; the notch pill is
always painted by WPF, since a transparent window rules a backdrop out.)

## Layout

```
windows/
  Codenotch.sln                 Two projects + test project
  Directory.Build.props         Shared build settings
  install.ps1                   Per-user install/uninstall
  install.cmd                   Double-clickable wrapper around install.ps1
  get.ps1                       The "irm ... | iex" bootstrap: download, verify, install
  src/Codenotch.Core/           Models, parsing, providers, presentation, pace, settings
  src/Codenotch.App/            WPF: tray, flyout, notch, settings window, Win32 interop
  tests/Codenotch.Core.Tests/   xunit tests + fixtures
```

MIT licensed, like the rest of the repo.
