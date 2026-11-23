# LighterSpotify

A small Windows mini-player for Spotify. It shows what's playing, has playback controls and song search, and plays audio through [librespot](https://github.com/librespot-org/librespot) instead of the official Chromium-based app.

Total memory use is roughly **60–80 MB** (the WPF window plus librespot), compared with 300 MB or more for the official app. CPU use is close to zero while idle.

## How it works

- **librespot** runs hidden in the background and appears in Spotify as a speaker called `LighterSpotify`.
- **The app** shows the player and controls playback through the Spotify Web API.
- **Your phone** sees `LighterSpotify` as a normal speaker, so you manage the queue there.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build the app, or only the .NET 8 Desktop Runtime to run a build
- **Spotify Premium.** It's required both by librespot and by Spotify's Development Mode rules for API apps.

## Setup

### 1. Create a Spotify app (one-time)
1. Go to <https://developer.spotify.com/dashboard> and click **Create app**.
2. Add this **Redirect URI**: `http://127.0.0.1:8888/callback`. Use the IP address exactly as written, not `localhost`.
3. Under APIs, tick **Web API**.
4. Save, then copy the **Client ID**.

### 2. Get librespot.exe
librespot doesn't publish Windows builds, so you build it once with Rust:

```powershell
winget install Rustlang.Rustup
cargo install librespot --locked
```

This puts `librespot.exe` in `%USERPROFILE%\.cargo\bin`, where the app finds it automatically. If the build asks for missing tools, install **Visual Studio Build Tools (Desktop C++)**, and CMake/NASM if it asks for those too.

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
| Pin | Keep the window on top of others |
| Hide (–) | Hide to the system tray. Left-click the tray icon to show it again. |
| Media keys | Play/pause, next and previous. You can turn this off in Settings. |

Drag anywhere on the window to move it.

## Notes

- **Media keys:** when they're on, the app captures them, so other apps (for example YouTube in a browser) won't receive them. Untick the option in Settings if that's a problem.
- **Polling:** the app checks Spotify's player state every 6 s, and only while the window is visible. Between checks, the progress bar moves on a local timer. This keeps the app under Spotify's Development Mode request limits.
- **Search:** Development Mode returns at most 10 results per request, so use **Load more** for further results.
- **No disk cache:** librespot runs with `--disable-audio-cache`, so nothing is stored offline.
- **Your data:** settings, the encrypted login token (protected with Windows DPAPI), the librespot login and `error.log` are stored in `%AppData%\LighterSpotify`.
- **Unofficial:** librespot is an unofficial client and is against Spotify's terms of use. It's meant for personal use only and can break when Spotify changes things.
