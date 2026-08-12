# AGC Guard

A small Windows system-tray utility for ham radio operators: it watches for your
transceiver's USB audio codec being plugged in and automatically turns off
Windows' built-in audio "enhancements" (which include automatic gain control,
noise suppression, loudness equalization, etc.) on both the speaker and
microphone side. Those enhancements actively fight the radio's own AGC and can
badly distort FT8/SSB/etc. audio, so most digital-mode setups want them
permanently off for the transceiver's audio device - this app makes sure they
stay off even after a reboot, a driver update, or unplugging/replugging the
radio.

## What it does

- Runs quietly in the system tray. Left/double-click or the context menu opens
  the device manager; right-click gives you the full menu.
- You tell it which USB audio device(s) to watch, identified by USB
  Vendor/Product ID (not by USB port), via a small **wizard**:
  "Disconnect the radio → click Next → reconnect the radio" and it
  auto-detects the new audio endpoint(s) and offers to add them.
- Whenever a *watched* USB audio device is plugged in (or is already
  connected at app startup), it disables the "Disable all enhancements"
  property on that endpoint - for both the render (speaker) and capture
  (microphone) side if the device exposes both.
- Shows a small non-intrusive balloon/toast notification near the tray icon
  whenever it actually disables something.
- Lets you enable/disable monitoring, and add/remove watched devices, from
  the tray menu.
- Can register itself to start with Windows (HKCU `Run` key - no admin
  rights required).
- Logs everything (including problems) to
  `%AppData%\Hamstuff\AgcGuard\logs\agcguard-YYYYMMDD.log`.

## How the "disable enhancements" part actually works

Every Windows audio endpoint (each speaker/microphone the OS knows about) has
a property called `PKEY_AudioEndpoint_Disable_SysFx`. This is exactly the
property behind the classic Sound control panel's **Enhancements tab →
"Disable all enhancements"** checkbox, and the modern Settings app's
**"Enhance audio"** toggle. Hamstuff AGC Guard sets that property to `1`
(disabled) on the matching endpoint(s) via the standard `IMMDeviceEnumerator`
/ `IPropertyStore` COM APIs - the same mechanism the Sound control panel
itself uses. No registry hacking, no undocumented APIs, no third-party
drivers.

**Limitation:** this only affects effects implemented through Windows' audio
effects/APO framework (what the Enhancements tab / "Enhance audio" toggle
controls). It does not - and cannot - change anything inside the radio's own
hardware DSP.

## Device identification

Each monitored device is stored as a USB `VID_xxxx&PID_xxxx` pair, extracted
from the audio endpoint's underlying PnP device instance ID. That means it
matches the *type* of device regardless of which USB port you plug it into
(and regardless of whether Windows assigns it a new endpoint ID after a
driver update).

## Building

Requires the .NET 8 SDK and Windows (WinForms + the Core Audio COM APIs used
here are Windows-only).

```powershell
dotnet build -c Release
```

The output executable is `HamstuffAgcGuard.exe`. No NuGet packages are
required - all Windows COM interop is hand-written against the public,
documented Core Audio interfaces, so the project builds fully offline.

## Using it

1. Launch `HamstuffAgcGuard.exe`. It appears in the system tray (you may need
   to expand the hidden icons area the first time).
2. Right-click the tray icon → **Manage Devices...** → **Add via Wizard...**
3. Follow the 3-step wizard: disconnect the radio's USB audio interface,
   click Next, then plug it back in. The wizard detects the new endpoint(s)
   automatically and lets you pick which one(s) to add.
4. That's it - from now on, whenever that device is connected (including
   right now, and on every future plug-in or app/Windows restart), its
   Windows audio enhancements/AGC get switched off automatically, with a
   quick toast confirming it.
5. Optional: right-click → **Start with Windows** to have it launch at
   sign-in automatically.

To stop watching a device, open **Manage Devices...** and remove it from the
list. To pause monitoring entirely without removing your device list,
uncheck **Monitoring Enabled** in the tray menu.

## Troubleshooting

Check `%AppData%\Hamstuff\AgcGuard\logs\` (also reachable via the tray menu's
**Open Log Folder**) for a dated log file with details on any failures
(e.g. a device that disappeared mid-operation, or a property that couldn't
be written).
