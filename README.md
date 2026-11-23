# LighterSpotify

A small Windows mini-player for Spotify. It shows what's playing, has playback controls and song search, and plays audio through [librespot](https://github.com/librespot-org/librespot) instead of the official Chromium-based app.

Total memory use is roughly **60–80 MB** (the WPF window plus librespot), compared with 300 MB or more for the official app. CPU use is close to zero while idle.

## How it works

- **librespot** runs hidden in the background and appears in Spotify as a speaker called `LighterSpotify`.
- **The app** shows the player and controls playback through the Spotify Web API.
- **Your phone** sees `LighterSpotify` as a normal speaker, so you manage the queue there.

## Requirements

- Windows 10 (version 1809 or later) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build the app, or only the .NET 8 Desktop Runtime to run a build
- **Spotify Premium.** It's required both by librespot and by Spotify's Development Mode rules for API apps.

## Setup

### 1. Create a Spotify app (one-time)
1. Go to <https://developer.spotify.com/dashboard> and click **Create app**.
2. Add this **Redirect URI**: `http://127.0.0.1:8888/callback`. Use the IP address exactly as written, not `localhost`.
3. Under APIs, tick **Web API**.
4. Save, then copy the **Client ID**.

### 2. Get librespot.exe
librespot doesn't publish Windows builds, so you build it once with Rust (tested with librespot v0.8.0).

1. **Install Rust and the C++ build tools.** Rust needs the C++ tools for the MSVC linker (`link.exe`); approve the admin prompt if one appears.
   ```powershell
   winget install Rustlang.Rustup
   winget install Microsoft.VisualStudio.2022.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
   ```
2. **Close PowerShell and open a new window**, so the new PATH loads. Check with `cargo --version`. If there's no default toolchain, run `rustup default stable-msvc`.
3. **Build librespot.** This takes about 5–10 minutes:
   ```powershell
   cargo install librespot --locked
   ```

This puts `librespot.exe` in `%USERPROFILE%\.cargo\bin`, where the app finds it automatically.

**Troubleshooting**

| Error | Fix |
|---|---|
| `cargo` is not recognized | Open a new PowerShell window. |
| `linker 'link.exe' not found` | The C++ build tools are missing. Run the Build Tools command above. If winget says Build Tools is already installed, open **Visual Studio Installer** → **Modify** → tick **Desktop development with C++** → **Modify**. Then open a new window and build again. |

### 3. Build and run the app
```powershell
git clone https://github.com/heli0hari/lighterspotify.git
cd lighterspotify
dotnet publish -c Release -r win-x64 --self-contained false -o publish
.\publish\LighterSpotify.exe
```

### 4. First launch
1. Paste your **Client ID** into Settings and click **Save**.
2. Click **Log in with Spotify** and approve access in the browser.
3. A librespot console window opens **once** for librespot's own Spotify login. If no browser opens, copy the link it shows. After you sign in, the app restarts librespot hidden.
4. Press the speaker button (**Play on this PC**), or pick `LighterSpotify` as the device on your phone.

## Controls

| Control | Action |
|---|---|
| Search icon | Search songs. Click a result to play it; press **+** to add it to the queue. |
| Speaker icon | Move playback to this PC |
| Pin | Keep the window on top of others. While pinned, it turns into frosted glass (the desktop behind it is blurred) and gets darker when you hover over it. You can change the tint with `GlassTintPercent` (10–100, default 50) in `settings.json`. |
| Lock (only while pinned) | Disables the playback controls, seek bar, search and dragging, so stray clicks don't do anything. Media keys, the Windows flyout and the tray menu still work. Unpinning also unlocks. |
| Hide (–) | Hide to the system tray. Left-click the tray icon to show it again. |
| Windows media flyout | The app appears in the speaker/volume flyout and on the lock screen, with play/pause, next and previous. Keyboard media keys work through it too. |

Drag anywhere on the window to move it.

## Start with Windows

Tick **Start with Windows** in Settings and click **Save**. The app then starts hidden in the tray when you log in. It uses your account's Run registry key, so no admin rights are needed.

There's no installer, so the app lives in the `publish` folder. To add it to the Start menu, run this once:
```powershell
$s = (New-Object -ComObject WScript.Shell).CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\LighterSpotify.lnk")
$s.TargetPath = "$HOME\lighterspotify\publish\LighterSpotify.exe"; $s.Save()
```

## Notes

- **Media keys:** normally they reach the app through the Windows media flyout, so other apps keep working too. If Windows sends them to a different player, tick **Capture media keys** in Settings. That forces them to this app, but blocks them in other apps (for example YouTube in a browser).
- **Polling:** the app checks Spotify's player state every 6 s while the window is visible, and every 15 s while it's hidden in the tray (to keep the Windows flyout current). Between checks, the progress bar moves on a local timer. This keeps the app under Spotify's Development Mode request limits.
- **Search:** Development Mode returns at most 10 results per request, so use **Load more** for further results.
- **No disk cache:** librespot runs with `--disable-audio-cache`, so nothing is stored offline.
- **Your data:** settings, the encrypted login token (protected with Windows DPAPI), the librespot login and `error.log` are stored in `%AppData%\LighterSpotify`.
- **Unofficial:** librespot is an unofficial client and is against Spotify's terms of use. It's meant for personal use only and can break when Spotify changes things.
