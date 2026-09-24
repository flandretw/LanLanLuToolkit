<p align="center">
  <img src="lanlanlu-toolkit/Assets/AppIcon.png" alt="LanLanLu Toolkit Icon" width="128">
</p>

# LanLanLu Toolkit

[English](README.md) | [臺灣正體中文](README-zh_TW.md)

> **Disclaimer**  
> This project was forged using **Gemini "Vibe Coding"**, fueled by AI magic and excessive amounts of digital fries. **Proceed with caution**! If the UI starts dancing or the code looks like a magical incantation, don't worry—it's just the vibe.

"Lan Lan Lu! Your Windows experience just got a dose of madness!"

## What is this project?

We hate bloated installers, heavy background services, and leftover registry junk. LanLanLu Toolkit is a pure, portable toolbox built on WinUI 3—drop it onto a USB flash drive, plug it in, and run your diagnostics anywhere. Combining modern fluent animations with the raw energy of 2000s meme culture, it delivers essential system maintenance and peripheral diagnostics without leaving a trace on your machine.

## Features

* **Hardware Performance Monitoring (Experimental)**  
  Provides a quick glance at CPU, GPU, RAM, and disk utilization, clock speeds, VRAM, and temperatures without opening Task Manager. This feature is currently experimental; for comprehensive diagnostics, Windows Task Manager remains the recommended choice.

* **Peripheral Diagnostics: Keyboard NKRO & Mouse Chatter Detection**  
  Tailored for gamers and hardware enthusiasts with 60 fps responsive vector layouts and acoustic key feedback.
  * Keyboard Testing: Live switching between 104 full-size, 87-key TKL, and 61-key compact layouts with NKRO simultaneous key tracking, Virtual Key (VK) inspection, and key chatter warnings to verify rollover on new or secondhand gear.
  * Mouse Diagnostics: Microswitch click counters, real-time polling rate (Hz) calculation, scroll wheel delta measurement, trajectory canvas, and customizable switch chatter debounce thresholds to detect double-click degradation on gaming mice up to 1000 Hz+.

* **Crash Analysis: Minidump & BugCheck Parsing**  
  Investigate system crashes directly in a clean UI without installing gigabytes of debugging toolkits.
  * Minidump Analysis: Automatically reads `C:\Windows\Minidump`, parsing BugCheck codes, timestamps, and offending driver modules (`.sys`).
  * Critical Event Filtering: Integrates with Windows Event Log to extract recent critical crash events, helping pinpoint faulty drivers or hardware conflicts immediately after a sudden reboot.

* **System Repair: One-Click DISM & SFC Recovery**  
  One-click access to DISM and SFC repair tools without opening the command prompt, helping resolve stuck Windows updates or corrupted system files.
  * DISM Image Repair: Execute CheckHealth, ScanHealth, and online RestoreHealth.
  * SFC System File Repair: Run `SFC /scannow` to verify and repair core system integrity.
  * Component Store Cleanup: Automate cleanup of superseded update caches and the WinSxS store.

* **File Hash Verification: SHA-3 Support & Fast Comparison**  
  Multi-threaded high-speed file checksum generation supporting MD5, SHA-1, SHA-256, SHA-384, SHA-512, and SHA-3. Features drag-and-drop support and instant comparison fields with color feedback to verify download integrity.

* **File Association Fixer: Default App Inspection & Recovery**  
  Inspect registered default handler applications for common text, archive, media, and code formats, allowing quick recovery when default file associations or icons are hijacked.

* **Modern Fluent Interface**  
  Built on WinUI 3 with Mica backdrop effects, featuring smooth animations and dynamic cards that scale seamlessly with your window.

* **Theme Support**  
  Full support for light and dark themes with adaptive contrast and optimal readability across environments.

* **100% Portable**  
  Zero installer needed. Simply extract and run without touching your system registry.

* **Localization Support**  
  Fully localized in English and Traditional Chinese (Taiwan).

## Setup & Usage

1. Go to the [Releases](https://github.com/flandretw/lanlanlu-toolkit/releases) page and grab the latest `.zip`.
2. Extract it to wherever you want—Desktop, USB, or your folder.
3. Run `lanlanlu-toolkit.exe` and let the magic begin!

## Building from Source

If you have **Visual Studio 2026** (with Windows SDK 10.0.19041.0) and the spirit of adventure, you can build your own version:

```powershell
# Pack the magic into a portable folder (Defaults to x64)
.\scripts\Build-Portable.ps1

# Target a different architecture (Arch: x64, arm64)
.\scripts\Build-Portable.ps1 -Arch arm64
```

## FAQ

**Q: Is it safe to use?**  
A: Safer than a Masala burger made by a Microsoft employee! It is open-source, portable, and respects your system boundaries.

**License & Copyright**  
Copyright © 2026 flandretw | This project is licensed under the [MIT License](LICENSE).