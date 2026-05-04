# WinPods

WinPods is a small Windows tray app for viewing Apple AirPods battery information from Bluetooth Low Energy advertisements.

It scans nearby Apple/Beats BLE packets, lets you pick your own AirPods from a readable device list, and keeps the selected battery status available from the system tray.

## Features

- Windows tray icon with battery state
- AirPods/Beats BLE scanner
- Device picker for busy places with many nearby headphones
- Left, right, and case battery display
- Charging state display when present in the BLE packet
- Refresh button for quick rescans
- Hide-to-tray behavior when closing the window

## Requirements

- Windows 10 2004 or newer, or Windows 11
- Bluetooth adapter with BLE support
- .NET 10 SDK

## Run

```powershell
dotnet run
```

Or run the built executable:

```powershell
.\bin\Debug\net10.0-windows10.0.19041.0\WinPods.exe
```

## How To Use

1. Start WinPods.
2. Open your AirPods case near the PC.
3. Wait a few seconds for BLE packets to appear.
4. Select your headphones from the left device list.
5. Close the window if you want WinPods to stay in the tray.

## Notes

AirPods battery data is read from BLE advertisements, not from the normal Windows Bluetooth audio profile.

Battery values are usually broadcast in 10% steps. Case battery may only appear when the case is open or when at least one earbud is inside.

## Project Status

Early Windows utility. Built as a simple WinForms app.
