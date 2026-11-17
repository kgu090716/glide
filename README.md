<h1 align="center">
  <img src="docs/media/glide-banner.png" alt="Glide" width="840">
</h1>

<p align="center">
  <strong>English</strong> · <a href="README.ko.md">Korean</a>
</p>

Glide lets you **move, resize, and snap windows with three fingers** on a Windows Precision Touchpad. To move a window, place the pointer anywhere on it and drag three fingers together without clicking.

> Glide was built from start to finish through AI-assisted vibe coding. AI was also used to create this README and the demo videos. The source code has not been independently verified, so you may encounter unexpected issues.

## See Glide in action

[![Glide intro showing a window following a three-finger gesture](docs/media/glide-intro-en.gif)](docs/media/glide-gesture-guide-en.mp4)

[Watch the full gesture guide (MP4, 46 seconds)](docs/media/glide-gesture-guide-en.mp4)

## App screenshot

<p align="center">
  <img src="docs/media/glide-screenshot-en.png" alt="Glide 3.0.2 settings in English, showing the language selector and drag settings" width="640">
</p>

## Download and install

Choose the installer or the portable ZIP from [GitHub Releases](https://github.com/kgu090716/glide/releases).

| File | How to use it |
| --- | --- |
| `Glide-3.0.2-Setup.exe` | Run the installer. You can enable launch at sign-in during setup or under **Convenience** in Glide. |
| `Glide-3.0.2-Portable.zip` | Extract the entire archive into a folder and run `ThreeFingerWindowDrag.exe`. |

Both downloads include the .NET and WinUI runtimes.

> **Windows SmartScreen** may show a warning when you run Glide. The installer and portable executable are unsigned.

## System requirements

- Glide is designed for **Windows 11 x64** laptops with a **Windows Precision Touchpad** that reports three simultaneous finger contacts.
- The installer requires at least **Windows 10 version 2004 (build 19041)**. Installation and launch were checked on the Windows 11 development laptop. Other touchpad models, Windows 10, and ARM64 have not been verified directly.
- A touchpad detected only as a mouse may fail to report three-finger input. The same can happen in some Remote Desktop sessions and virtual machines.

## First launch

1. Select **Open touchpad settings** in Glide's first-launch dialog.
2. In Windows, open **Settings → Bluetooth & devices → Touchpad → Three-finger gestures**. Set both **Swipes** and **Taps** to **Nothing** so Windows does not perform its own actions during a Glide gesture.
3. In Glide, select **Get started**. Hover over a window and slide three fingers across the touchpad.

Choose English or Korean under **Language** in Glide. The change takes effect immediately and is saved for future launches.

## Gestures

| Action | Touchpad gesture | GIF |
| --- | --- | --- |
| Move a window | Slide three fingers together. Enable **Move the cursor too** to keep the cursor at the same relative position inside the moving window. | <img src="docs/media/gestures-en/01-drag.gif" width="400" alt="Move"> |
| Enlarge a window | Spread three fingers apart. | <img src="docs/media/gestures-en/02-spread.gif" width="400" alt="Enlarge"> |
| Shrink a window | Pinch three fingers together. | <img src="docs/media/gestures-en/03-pinch.gif" width="400" alt="Shrink"> |
| Resize from the edges | Hold three fingers still, then slide. The default hold time is one second. You can adjust it in the **Hold time** field. | <img src="docs/media/gestures-en/04-hold.gif" width="400" alt="Directional resize"> |
| Maximize a window | Drag the window to the top edge of the screen and lift your fingers. | <img src="docs/media/gestures-en/05-maximize.gif" width="400" alt="Maximize at the top edge"> |
| Snap a window to one half | Drag the window to the left or right edge of the screen and lift your fingers. | <img src="docs/media/gestures-en/06-side-snap.gif" width="400" alt="Snap and restore"> |
| Snap with a quick flick | Touch down with three fingers, flick, and lift within **0.35 seconds**. **↖, ↑, and ↗** maximize the window. **← and →** snap it to the corresponding half. | <img src="docs/media/gestures-en/07-flick.gif" width="400" alt="Quick flick"> |

Drag a window away after snapping it with Glide to restore its previous size. You can turn gestures on or off individually and adjust drag speed and flick sensitivity.

- Press **Ctrl + Alt + F9** to pause or resume gestures. Press **Esc** to cancel an active gesture.
- Closing the settings window hides Glide in the Windows notification area. To quit, right-click the Glide tray icon and select **Exit**.
- To control an elevated window such as Task Manager, select **Convenience → Restart as administrator** in Glide.

## Troubleshooting

If Glide does not detect the touchpad, select **Touchpad → Scan again**. If Task View or app switching opens during a Glide gesture, check the Windows touchpad settings from [First launch](#first-launch).

## How Glide was built

Glide started as a personal project to bring three-finger window dragging to Windows. I chose the features and refined how they work by using them on my laptop. I used OpenAI Codex to implement the code, debug it, and write tests.
