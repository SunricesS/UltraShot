<div align="center">

# 📸 UltraShoot

**Lightning-fast, ultra-lightweight Windows screenshot tool & annotation editor.**  
*Windows için ultra hızlı, düşük bellek tüketen ekran alıntısı ve görsel düzenleme aracı.*

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue?logo=windows)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Portable](https://img.shields.io/badge/Release-Single%20File%20Portable-orange)](releases/)

</div>

---

## ⚡ Highlights / Öne Çıkan Özellikler

* 🚀 **Zero-Lag Overlay (< 10 ms):** Directly captures the virtual desktop using low-level Win32 GDI `BitBlt` calls. No UWP/WinRT bloat, no 1-2 second startup lag.
* 📋 **Instant Clipboard Copy:** The moment you release your mouse after selecting an area, the image is synchronously placed onto your Windows Clipboard (`Ctrl+V` anywhere immediately).
* 🪶 **Minimal Resource Footprint (~25 MB RAM, 0% CPU):** Pure event-driven Windows hotkey messaging (`WM_HOTKEY`). Automatic working-set memory trimming when idling in the system tray.
* 🖥️ **Full Screen, Multi-Monitor & DPI-Aware:** Compatible with borderless fullscreen games, multiple monitors with different DPI scaling (100%, 125%, 150%, 4K).
* 🗂️ **Automated Unique Naming:** Screenshots are automatically saved to your `Pictures\UltraShoot` folder with millisecond-precision timestamps (`UltraShoot_yyyy-MM-dd_HH-mm-ss-fff.png`).
* 🔔 **System Tray Gallery / Drawer:**
  * Shows recent screenshots in a sleek bottom-right floating card.
  * Direct thumbnail previews, dimensions, and quick copy/edit/delete buttons.
  * **Tray Right-Click Menu:** "Open Screenshots Folder", "Settings & Hotkey", "Recent Screenshots", and "Exit".
* 🎨 **Integrated Annotation & Cropping Editor:**
  * ✏️ **Pen:** Freehand drawing with adjustable stroke width and vibrant color palette.
  * 🖍️ **Highlighter:** Translucent marker for highlighting text and documents.
  * ➡️ **Arrow & ⬜ Shapes:** Add crisp direction arrows and highlight boxes.
  * ✂️ **Crop Tool:** Interactively refine and crop screenshots.
  * ↩️ **Undo (Ctrl+Z):** Revert actions step by step.
  * 💾 **Save As:** Export to PNG, JPG, or BMP.
* ⚙️ **Customizable Global Hotkey:** Change the shortcut in settings simply by pressing your preferred key combination (e.g., `Ctrl+Shift+S`, `PrintScreen`, `Alt+A`).

---

## 📥 Download & Portability / Taşınabilir Sürüm

No installation or prerequisites required! UltraShoot can be downloaded as a **Self-Contained Single-File Executable**:
* Works out of the box on any 64-bit Windows 10 / 11 PC.
* **Does not require .NET SDK or .NET Runtime to be installed.**

You can find the standalone portable executable in:
👉 [`releases/UltraShoot-v1.0.0-win-x64.exe`](releases/UltraShoot-v1.0.0-win-x64.exe)

---

## ⌨️ Keyboard Shortcuts / Kısayollar

| Shortcut | Action |
|---|---|
| `Ctrl + Shift + S` (Default) | Trigger instant screen selection tool |
| `Left Mouse Drag` | Select screen region |
| `Mouse Release` | Instant clipboard copy & auto-save |
| `ESC` or `Right Click` | Cancel selection overlay |
| `Ctrl + Z` (In Editor) | Undo last drawing stroke / shape |
| `Ctrl + C` (In Editor) | Copy edited image to clipboard |
| `Ctrl + S` (In Editor) | Save / overwrite image |

---

## 🛠️ Building from Source / Kaynaktan Derleme

### Requirements
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* Windows 10 (1809+) or Windows 11

### 1. Standard Framework-Dependent Build (Ultra-compact ~160 KB)
```powershell
dotnet build -c Release
dotnet publish -c Release -o ./publish
```

### 2. Standalone Portable Build (Runs on any PC without .NET runtime)
```powershell
dotnet publish src/UltraShoot/UltraShoot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./publish-standalone
```

---

## 📁 Project Architecture / Proje Yapısı

```
UltraShoot/
├── src/
│   └── UltraShoot/
│       ├── Core/
│       │   ├── NativeMethods.cs         # Win32 GDI, BitBlt, RegisterHotKey interop
│       │   ├── HotkeyManager.cs         # Global hotkey message window hook
│       │   ├── ScreenCaptureService.cs  # Multi-monitor DPI-aware screen capture
│       │   ├── StorageService.cs        # Lock-free file I/O & naming engine
│       │   └── SettingsManager.cs       # JSON configuration & Windows startup registry
│       ├── Models/
│       │   ├── AppSettings.cs           # User configuration model
│       │   └── ScreenshotItem.cs        # Gallery thumbnail & metadata model
│       ├── UI/
│       │   ├── OverlayWindow.xaml       # Instant darkened cutout selection window
│       │   ├── RecentFlyoutWindow.xaml  # Bottom-right tray drawer gallery
│       │   ├── EditorWindow.xaml        # Drawing, arrow, crop & Save As editor
│       │   ├── SettingsWindow.xaml      # Interactive hotkey recorder & settings
│       │   └── TrayIconManager.cs       # System tray icon & context menus
│       ├── App.xaml / App.xaml.cs       # Single-instance mutex, app lifecycle
│       └── app.manifest                 # PerMonitorV2 high-DPI configuration
├── .github/
│   └── workflows/
│       └── build-and-release.yml        # Automated CI/CD release workflow
├── releases/                            # Standalone portable binaries
├── .gitignore
├── LICENSE
└── README.md
```

---

## 📄 License
This project is licensed under the [MIT License](LICENSE).
