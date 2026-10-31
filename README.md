# UltraShot 
**Ultra-fast, low-resource screenshot and annotation tool for Windows**

UltraShot is a high-performance, lightweight Windows screenshot tool engineered with .NET 10, WPF, and native Win32 APIs. It provides an instant alternative to Windows Snipping Tool, eliminating lag, heavy memory usage, and background overhead.

---

##  Key Features

-  **Ultra-Fast Desktop Capture (< 10 ms):** Direct Win32 GDI `BitBlt` virtual desktop snapshot executed immediately upon hotkey trigger, freezing screens seamlessly with zero delay.
-  **Instant Clipboard Copy:** Microseconds after you release the left mouse button (`MouseUp`), the captured region is written directly to the Windows Clipboard ready for immediate `Ctrl + V` pasting.
-  **Minimal Resource Footprint (~15–20 MB RAM, 0% CPU):** Operates on an event-driven Windows message loop (`WH_KEYBOARD_LL` / `WM_HOTKEY`) with no polling timers. Memory is automatically trimmed after every operation.
-  **Full Windows Key Combination Support:** Supports custom shortcuts combining the Windows key (e.g. `Win + S`, `Win + Shift + S`, `Win + A`, `PrintScreen`) without Start menu interference or sticky key issues.
- ️**Configurable Image Formats & Compression:**
  - **PNG:** Lossless clarity (Default).
  - **JPEG:** High-efficiency compression with customizable quality level (10%–100%) to drastically reduce file sizes.
  - **BMP:** Uncompressed raw bitmap.
- 📋 **Temporary Mode (Clipboard-Only):** Toggle to capture temporary screenshots copied only to clipboard without saving files to disk.
- ⚙️ **Configurable Post-Capture Action:** Choose what happens immediately after taking a screenshot:
  - **Show Recent Screenshots Drawer (Default):** Quick drawer opens at tray with actions to copy, edit, and delete.
  - **Show Windows Notification (Click to Edit):** Displays a Windows notification in the bottom-right corner; clicking it opens the screenshot directly in the editor!
  - **Do Nothing (Quiet Mode):** Captures and copies/saves silently with zero interruptions.
- 🔔 **Recent Screenshots Drawer & In-Place Confirmation:**
  - Quick flyout panel displaying recent screenshots with thumbnails, creation timestamps, and dimensions.
  - Quick action buttons to Edit, Copy, and Delete.
  - Sleek, theme-consistent in-place delete confirmation with optional prompt setting.
-  **Built-In Image Editor:**
  -  **Pen:** Freehand drawing with custom colors and stroke thicknesses.
  -  **Highlighter:** Semi-transparent highlighter for text and points of interest.
  -  **Arrow:** Crisp directional pointer arrows.
  -  **Rectangle:** Clean boundary box framing.
  -  **Crop:** Re-crop screenshots with real-time dashed guides.
  -  **Undo (Ctrl + Z):** Step-by-step history undo.
  -  **Save & Save As:** Quickly overwrite or export in PNG, JPEG, or BMP formats.
-  **Multi-Monitor & DPI Aware:** Full `PerMonitorV2` support across mixed-DPI multi-monitor setups and 4K displays.

---


### Default Shortcuts
- **Ctrl + Shift + S** (or configured custom hotkey): Instantly initiates screen capture.
- **Left Mouse Click & Drag:** Select region. Releasing copies the image to clipboard and saves it.
- **ESC or Right Click:** Cancel screen selection immediately.
- **Left Click System Tray Icon:** Opens the Recent Screenshots panel.
- **Right Click System Tray Icon:** Context menu (*Take Screenshot*, *Recent Screenshots*, *Open Screenshots Folder*, *Save to Clipboard Only*, *Settings & Hotkey*, *Exit*).

---

## Installation

### Option 1: Direct Download (Recommended)
Download the latest standalone executable or installer directly from the [**Releases**](https://github.com/SunricesS/UltraShot/releases/latest) page.

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- Windows 10 / 11 (x64 / arm64)

### Build Commands
```powershell
# Restore dependencies and build in Release mode
dotnet build UltraShot.slnx -c Release

# Publish self-contained single-file binary
dotnet publish src\UltraShot\UltraShot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-standalone
```

---

##  License
UltraShot is released under the MIT License.
