# WinPods

WinPods is a Windows tray app for Apple AirPods. It shows battery info, helps pick the correct AirPods in busy Bluetooth areas, and can switch AirPods Pro listening modes from Windows.

## Features

- Windows tray icon
- AirPods/Beats scanner
- Merged device list for earbuds/case/rotating BLE addresses
- Highlights the AirPods currently connected in Windows Bluetooth
- Pin your own AirPods and optionally hide nearby foreign AirPods
- Left, right, and case battery display
- Exact 1% battery when MagicAAP driver is available
- BLE battery fallback when only advertisements are available
- Modern light/dark UI with saved theme
- Saved window size, position, splitter layout, and pinned-device filter
- Listening mode controls:
  - Transparency
  - Adaptive
  - Noise Cancellation
- Call Quality Guard:
  - Detects AirPods/Beats Hands-Free call audio risk on Windows
  - Warns when AirPods mic can force low-quality headset audio
  - Can route output to AirPods stereo and mic to a non-AirPods microphone
  - Tray menu controls and Sound settings shortcut
- Sortable device list
- Start/stop scan button
- Auto-pauses scanning after AirPods are connected and the app is in tray
- Hide-to-tray behavior
- Hidden desktop launcher with app icon

## Requirements

- Windows 10 2004 or newer, or Windows 11
- Bluetooth adapter
- .NET 10 SDK to run from source
- MagicAAP driver for exact battery and listening mode control

## Install MagicAAP Driver

WinPods uses the MagicAAP driver for AirPods control commands on Windows. Without this driver, Windows Bluetooth audio still works, but exact battery and listening mode switching may not work.

Driver source:
- [MagicAAP driver page](https://magicpods.app/magicaap/)
- [Official MagicAAP install docs](https://help.magicpods.app/fun-magicaap-community/)

Open PowerShell as Administrator and run:

```powershell
irm "https://magicpods.app/utils/magicaap-community-v1.ps1" | iex
```

Choose install in the opened script window.

Windows Defender may warn or block the driver because it is a community driver. If Defender blocks it, allow it in Windows Security, then run the same install command again:

```powershell
irm "https://magicpods.app/utils/magicaap-community-v1.ps1" | iex
```

Restart Windows after the driver installs.

You can verify the driver with:

```powershell
pnputil /enum-drivers | Select-String -Pattern "magicaap|Maslov" -Context 0,6
```

Expected result should include `magicaap.inf` and `MagicAAP`.

## Run From Source

Clone the repo:

```powershell
git clone https://github.com/ValentinSK0/WinPods.git
cd WinPods
```

Run:

```powershell
dotnet run
```

Or run the helper script:

```powershell
.\Scripts\Run-WinPods-Hidden.ps1
```

Or build:

```powershell
dotnet build
.\bin\Debug\net10.0-windows10.0.19041.0\WinPods.exe
```

Create or refresh the desktop shortcut:

```powershell
.\Scripts\Create-Desktop-Shortcut.ps1
```

The shortcut runs WinPods without showing a console window.

## Build Installer

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then run:

```powershell
.\Scripts\Build-Installer.ps1
```

The script reads the app version from `WinPods.csproj`, publishes a self-contained `win-x64` build, and creates:

```text
dist\WinPodsSetup-0.2.0.exe
```

To override the version for one build:

```powershell
.\Scripts\Build-Installer.ps1 -Version 0.2.1
```

Build outputs in `publish\` and `dist\` are ignored by git. Upload the generated setup file to GitHub Releases, not to normal source commits.

## How To Use

1. Install MagicAAP driver.
2. Restart Windows.
3. Connect AirPods in Windows Bluetooth.
4. Start WinPods.
5. Pick your AirPods from the device list.
6. Use Pin as mine to prioritize your AirPods.
7. Enable Only my AirPods if you want to ignore nearby foreign AirPods.
8. Use Start scan or Stop scan to control scanning.
9. Use Listening mode buttons for Transparency, Adaptive, or Noise Cancellation.
10. Close the window to keep WinPods running in tray.

When your AirPods are connected and WinPods is hidden in tray, scanning auto-pauses after about 30 seconds. Opening the window starts scanning again.

WinPods stores local app preferences in:

```text
%LOCALAPPDATA%\WinPods\settings.json
```

Saved preferences include window layout, main splitter position, dark theme, pinned AirPods, pinned-only filter, and Call Quality Guard settings.

## Battery Notes

AirPods BLE advertisements usually expose battery in 10% steps. WinPods uses that as fallback.

When MagicAAP is working, WinPods reads AirPods battery notifications over AAP and can show exact 1% values for left, right, and case.

Case battery may appear only when the case is open or when at least one earbud is inside.

## Call Quality Guard Notes

Windows Bluetooth normally cannot use AirPods high-quality stereo audio and the AirPods microphone at the same time. If an app selects the AirPods Hands-Free microphone, Windows may switch audio to the low-quality headset profile.

Call Quality Guard monitors the default Windows communication audio devices. When it sees AirPods/Beats Hands-Free risk, it warns in the app and tray. The Fix route action tries to set:

- output: AirPods stereo endpoint
- microphone: laptop, webcam, USB, or other non-AirPods microphone

If Windows blocks automatic routing or no safe microphone is available, open Sound settings from WinPods and set those devices manually.

## Troubleshooting

If listening mode does not switch:

1. Confirm AirPods are connected in Windows Bluetooth.
2. Confirm MagicAAP is installed:

```powershell
pnputil /enum-drivers | Select-String -Pattern "magicaap|Maslov" -Context 0,6
```

3. Reconnect AirPods.
4. Restart WinPods.
5. Restart Windows if the driver was installed recently.

If Windows Defender blocked the driver during install, allow it in Windows Security and run the MagicAAP install command again.

If the desktop shortcut does not start WinPods, recreate it:

```powershell
.\Scripts\Create-Desktop-Shortcut.ps1
```

The shortcut should point to:

```text
Scripts\Run-WinPods-Hidden.ps1
```

## Project Structure

```text
AirPods\    AirPods AAP protocol, battery decoding, models
App\        Application entry point
Audio\      Windows audio endpoints and Call Quality Guard
Bluetooth\  BLE scanning, L2CAP socket, connected Bluetooth devices
Interop\    MagicAAP driver connection
Scripts\    Desktop shortcut and launcher scripts
Settings\   Local settings persistence
UI\         WinForms UI, theme controls, custom panels, tray icon
Assets\     App icon and visual assets
```

## Project Status

Early Windows utility. Built with WinForms and MagicAAP driver support.
