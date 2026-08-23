# LanLanLu Toolkit (蘭蘭露工具箱)

[English](README.md) | [臺灣正體中文](README-zh_TW.md)

"RAN RAN RU! Your WinUI 3 experience just got a dose of madness! 🤡🍟"

---

## 🌀 What is this chaotic masterpiece?

Are you tired of boring, gray, installer-bloated tools that take forever to set up? Do you crave the snap and pop of a modern Windows interface combined with the raw energy of a 2000s meme?

The **LanLanLu Toolkit** is born for this! Built on the cutting-edge **WinUI 3** framework, it brings you a premium, portable experience that lives entirely in your pocket or your USB drive. No installation, no registry junk, just pure, unadulterated tool magic.

> ⚠️ **Disclaimer**
> This project was forged using **Gemini "Vibe Coding"**, fueled by AI magic and excessive amounts of digital fries. **Proceed with caution**! If the UI starts dancing or the code looks like a magical incantation, don't worry—it's just the vibe 🪄

---

## 🔥 Crazy Magical Features

* 📊 **Hardware Performance Monitoring (Experimental)**
  We tried building hardware monitoring! While it's currently an experimental work-in-progress with rough usability and nowhere near the efficiency or polish of the native Windows Task Manager (seriously, just use Task Manager for serious diagnostics 🤡), it still gives you a quick snapshot of **CPU, GPU, RAM, and Disk** utilization, clock speeds, VRAM, and temperatures.
  > 💡 **Practical Use Case**: Quick casual glance at hardware specs without opening Task Manager, or exploring experimental hardware polling.

* ⌨️🖱️ **High-Velocity Input Diagnostics**
  Tailored for gamers and hardware enthusiasts with 60 fps responsive vector layouts and acoustic key feedback!
  * **Versatile Keyboard Testing**: Live switching between **104 Full-size / 87-key TKL / 61-key Compact** layouts, **NKRO (N-Key Rollover)** simultaneous key tracking, Virtual Key code (VK) inspection, and key chatter/interval warnings.
  * **Extreme Mouse Diagnostics**: Microswitch click counters, high-precision **Real-time Polling Rate (Hz)** calculator, scroll wheel delta measurement, **Switch Chatter Detection** (custom debounce threshold to spot double-click degradation), and real-time trajectory canvas.
  > 💡 **Practical Use Cases**: Verify NKRO on brand-new or second-hand keyboards, detect if an aging mouse switch is suffering from accidental double-clicking, and test if a gaming mouse sustains true 1000 Hz+ polling.

* 💥 **Crash & BSOD Destroyer**
  No need to download gigabytes of heavy debuggers; unravel system crash mysteries right inside a lightweight, elegant UI!
  * **Minidump Analysis**: Automatically scans `C:\Windows\Minidump`, parses BugCheck codes, timestamps, and offending driver modules (`.sys`).
  * **Critical Event Filtering**: Integrates with the Windows Event Log to extract recent critical crash events in real time.
  > 💡 **Practical Use Case**: When sudden Blue Screens (BSOD) or reboots happen, instantly pinpoint the faulty GPU driver, antivirus filter, or broken system component.

* 🛠️ **God-Tier System Repair**
  Windows acting up? Boom! One-click access to **DISM and SFC** repair spells.
  * **DISM Image Repair**: Execute CheckHealth, ScanHealth, and online RestoreHealth.
  * **SFC System File Repair**: One-click `SFC /scannow` to rescue corrupted Windows core system files before chaos takes over.
  * **Component Store Cleanup**: Automated cleanup of superseded update caches and WinSxS store.
  > 💡 **Practical Use Case**: Effortlessly resolve stuck Windows Updates or repair damaged system files without opening the Command Prompt.

* 🔐 **Warp-Speed File Hash Calculation**
  Multi-threaded, high-speed file checksum generation supporting **MD5, SHA-1, SHA-256, SHA-384, SHA-512, and SHA-3**.
  * Instant calculation with drag-and-drop support.
  * Built-in hash comparison field with instant color-coded match feedback.
  > 💡 **Practical Use Case**: Instantly verify downloaded Windows ISOs, installers, or backups against published checksums to guarantee data integrity.

* 📂 **File Association Caretaker**
  Inspect registered default handler applications for common text, archive, media, and code formats, allowing quick recovery of corrupted associations.
  > 💡 **Practical Use Case**: Quickly diagnose and restore hijacked default applications or broken file extension icons.

* 🌀 **Modern Chaos (WinUI 3 & Mica)**
  Smooth animations, **Mica effects**, and a layout that feels as fresh as a newly salted batch of fries! The dynamic hero dashboard and all cards adapt to your window size like magic.

* 🎨 **Dimensional Theme Strike**
  The entire app adapts instantly to your vibe! Whether you're in a light or dark dimension, our theme-aware UI ensures perfect contrast and readability.

* 📦 **No Strings Attached Portable (100% Portable)**
  We hate installers! This toolkit is **100% Portable**. Download, unzip, and run! It leaves zero registry footprint on your system, keeping your Windows as clean as a whistle.

* 🌐 **Global Madness Support**
  Fully localized for the world! Whether you speak English or Traditional Chinese (Taiwan), the madness is perfectly translated.

---

## 🛠️ Setup & Usage

1. Go to the [Releases](https://github.com/flandretw/lanlanlu-toolkit/releases) page and grab the latest `.zip`.
2. Extract it to wherever you want—Desktop, USB, or your secret folder.
3. Run `lanlanlu-toolkit.exe` and let the madness begin!

---

## 🏗️ Building from Source (For Wizards Only)

If you have **Visual Studio 2026** (with Windows SDK 10.0.19041.0) and the spirit of adventure, you can build your own version:

```powershell
# Pack the magic into a portable folder (Defaults to x64)
.\scripts\Build-Portable.ps1

# Target a different dimension (Arch: x64, arm64)
.\scripts\Build-Portable.ps1 -Arch arm64
```

---

## 🤔 FAQ

**Q: Is it safe to use?**  
A: Safer than a Masala burger made by a Microsoft employee! It's open-source, portable, and respects your system's boundaries.

---

**License & Copyright**  
Copyright © 2026 flandretw | This project is licensed under the [MIT License](LICENSE).