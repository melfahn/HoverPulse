# HoverPulse v1.0 — Feature Specification

**Status:** [DRAFT]
**License:** GPL-3.0
**Architecture:** Pure Win32 + .NET 8 (no XAML framework)

## Overview

HoverPulse is a free, open-source Windows accessibility tool that triggers mouse
clicks when the cursor remains stationary for a configurable duration. It is
**device-independent** — it works with any pointing device that moves the system
cursor (head trackers, eye trackers, joysticks, gyroscopic mice, standard mice, etc.).

HoverPulse does NOT move the cursor. It only watches cursor position and generates
synthetic click events. This separation is a core design principle.

## Target Users

- People with motor disabilities (ALS, cerebral palsy, spinal cord injury, muscular
  dystrophy, MS) using alternative pointing devices
- RSI / carpal tunnel sufferers who can move a mouse but find clicking painful
- Anyone using head trackers (QUHA Zono, HeadMouse Nano), eye trackers (Tobii),
  mouth/chin joysticks, sip-and-puff systems, or webcam-based tracking

## Target Platform

- Windows 10 version 1903+ and Windows 11
- .NET 8 runtime required
- x64 architecture (primary), ARM64 (secondary)

---

## Core Features (v1.0)

### F1: Dwell Detection Engine

The heart of the application. Continuously monitors cursor position and detects
when the cursor has come to rest.

- **Implementation:** Low-level mouse hook (`WH_MOUSE_LL` via `SetWindowsHookEx`)
  for immediate movement detection, with high-resolution timer for dwell countdown
- **Dwell Time:** Configurable from 0.1s to 5.0s in 0.1s increments. Default: 1.0s
- **Dwell Model:** Continuous (timer resets completely if cursor leaves tolerance zone)
- **Coordinate System:** Virtual desktop coordinates for multi-monitor support

### F2: Dwell Tolerance (Jitter Compensation)

Defines how many pixels the cursor may move during a dwell without resetting the timer.

- **Tolerance Radius:** Configurable from 1 to 50 pixels. Default: 10 pixels
- **Shape:** Circular (Euclidean distance from dwell start point)
- **Purpose:** Accommodates tremor, imprecise input devices, eye tracker jitter

### F3: Click Type Selection

Users select what type of click the next dwell will produce.

- **Click Types:**
  - Left click (default)
  - Right click
  - Double click
  - Drag (first dwell = mouse down at position, second dwell = mouse up)
- **Click Lock:** Option to repeat the same click type for consecutive actions
  without reverting to left click. Toggle on/off.
- **Selection UI:** Always-on-top toolbar with buttons for each click type

### F4: Visual Dwell Indicator

Real-time visual feedback showing dwell progress.

- **Design:** Circular progress ring drawn around the cursor position
- **Rendering:** Transparent topmost overlay window (`WS_EX_LAYERED | WS_EX_TRANSPARENT |
  WS_EX_TOPMOST`) with GDI+ or Direct2D rendering
- **Click-through:** Overlay is fully click-through — mouse events pass to windows beneath
- **Customization:**
  - Ring color (default: bright blue, configurable for visibility)
  - Ring size (radius in pixels)
  - Option to disable indicator entirely
- **Behavior:** Ring fills clockwise during dwell, resets when cursor moves,
  briefly flashes on click completion

### F5: Floating Toolbar

Small always-on-top panel for click type selection and controls.

- **Window Style:** `WS_EX_TOPMOST`, borderless, custom-drawn
- **Rendering:** GDI+ custom paint (no XAML)
- **Contents:**
  - Click type buttons (left, right, double, drag) with icons
  - Pause/resume toggle
  - Settings gear button
  - Current dwell time display
- **Behavior:**
  - Draggable to any screen position (position persisted)
  - Dockable to screen edges (snap within 10px)
  - Configurable orientation (horizontal or vertical)
  - Configurable opacity (30%-100%)
  - The toolbar itself responds to dwell clicks (for selecting click type)
- **Interaction:** Toolbar buttons are activated by dwelling on them (meta — the
  dwell clicker uses its own dwell mechanism to interact with its toolbar)

### F6: System Tray Integration

- **Implementation:** `Shell_NotifyIcon` Win32 API
- **Tray Icon:** Shows current state (active, paused) and current click type
- **Context Menu:** Enable/Disable, Click Types, Settings, About, Exit
- **Balloon Notifications:** State change notifications (optional)
- **Minimize to Tray:** Main toolbar can be hidden to tray

### F7: Rest / Pause (Midas Touch Mitigation)

Prevents unwanted clicks when the user is resting or reading.

- **Manual Pause:** Toggle via toolbar button, tray menu, or global hotkey
- **Rest Zone:** Configurable screen area where dwell is suppressed. User can
  place a rest zone corner of screen for parking the cursor.
- **Auto-Rest:** If cursor remains stationary beyond 2x the dwell time, suppress
  further clicks until cursor moves at least N pixels (configurable). This prevents
  rapid repeated clicking on the same spot.

### F8: Global Hotkeys

- **Toggle Enable/Disable:** Default: F9 (configurable)
- **Cycle Click Type:** Default: F10 (configurable)
- **Pause/Resume:** Default: F11 (configurable)
- **Implementation:** `RegisterHotKey` Win32 API
- **Requirement:** Hotkeys must work even when HoverPulse is not the focused window

### F9: Audio Feedback

- **Dwell Start Sound:** Subtle tick when dwell timer begins (optional)
- **Click Sound:** Distinct sound when click fires (optional)
- **Different sounds for different click types** (optional)
- **Volume:** Independent volume control or use system volume
- **Implementation:** `PlaySound` Win32 API or `System.Media.SoundPlayer`

### F10: Settings Persistence

- **Storage:** JSON file in `%APPDATA%\HoverPulse\settings.json`
- **Settings include:** Dwell time, tolerance, click type default, click lock state,
  toolbar position/orientation/opacity, hotkey bindings, audio on/off,
  indicator color/size, rest zone coordinates, auto-start preference
- **Auto-save:** Settings saved on change

### F11: Auto-Start with Windows

- **Implementation:** Registry key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  or Startup folder shortcut
- **Option:** Enabled/disabled via Settings
- **Startup behavior:** Start minimized to tray

---

## Non-Functional Requirements

### Performance
- **CPU:** < 1% CPU usage when idle (no cursor movement)
- **Memory:** < 15 MB working set
- **Input Latency:** < 5ms added latency to input pipeline from mouse hook
- **Startup:** < 500ms to operational state

### Accessibility
- **Keyboard-only operation:** All features accessible via keyboard
- **High contrast:** Toolbar and settings respect Windows high-contrast themes
- **Screen reader:** Toolbar buttons have accessible names

### Security
- **No elevation required** for standard operation
- **Future:** `uiAccess="true"` manifest for interacting with elevated windows
  (requires code signing and secure install location)
- **No network access:** Application is fully offline

### Deployment
- **Single-folder xcopy deployment** (no installer required for basic use)
- **Optional installer** for auto-start, Start Menu shortcut, uiAccess support
- **Size target:** < 10 MB total

---

## Architecture (Pure Win32 + .NET 8)

```
HoverPulse.exe
  |
  +-- Program.cs                    Entry point, message loop
  |
  +-- Core/
  |   +-- DwellDetector.cs          Cursor monitoring + dwell timing logic
  |   +-- ClickSimulator.cs         SendInput wrapper for synthetic clicks
  |   +-- MouseHook.cs              WH_MOUSE_LL global hook
  |   +-- HotkeyManager.cs         RegisterHotKey wrapper
  |
  +-- UI/
  |   +-- ToolbarWindow.cs          Floating toolbar (CreateWindowEx + GDI+ paint)
  |   +-- OverlayWindow.cs          Transparent click-through overlay (dwell ring)
  |   +-- SettingsWindow.cs         Settings dialog
  |   +-- TrayIcon.cs               System tray integration
  |
  +-- Services/
  |   +-- SettingsService.cs        JSON settings load/save
  |   +-- AudioService.cs           Sound playback
  |
  +-- Interop/
  |   +-- NativeMethods.cs          P/Invoke declarations (User32, Kernel32, Shell32)
  |   +-- WindowMessages.cs         WM_ constants
  |   +-- Structs.cs                POINT, RECT, INPUT, MSLLHOOKSTRUCT, etc.
  |
  +-- Resources/
      +-- Icons/                    Toolbar icons, tray icons
      +-- Sounds/                   Click sounds
```

### Key Win32 APIs Used

| API | Purpose |
|-----|---------|
| `SetWindowsHookEx(WH_MOUSE_LL)` | Global cursor position monitoring |
| `GetCursorPos` | Cursor position queries |
| `SendInput` | Synthetic mouse click injection |
| `CreateWindowEx` | Toolbar and overlay windows |
| `RegisterHotKey` | Global hotkey registration |
| `Shell_NotifyIcon` | System tray icon |
| `SetLayeredWindowAttributes` / `UpdateLayeredWindow` | Transparent overlay |
| `SetWindowPos` | Always-on-top z-order |
| `RegisterClassEx` / `DefWindowProc` | Window class and message handling |

---

## Future Features (Post v1.0)

- [ ] Per-application profiles (different dwell times per app)
- [ ] Exclusion zones (areas where dwell is suppressed)
- [ ] Target snapping (auto-snap to nearest clickable UI element via UI Automation)
- [ ] Stroke recognition (KMouseTool-style: rightward gesture = right-click)
- [ ] Scroll dwell (dwell at screen edges triggers scrolling)
- [ ] Portable mode (settings stored next to exe)
- [ ] Installer with uiAccess support
- [ ] Localization / multi-language
- [ ] Webcam-based head tracking integration (separate module)
