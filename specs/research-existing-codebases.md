# Existing Open-Source Codebases Survey

**Date:** 2026-02-20

## Dwell Clicker Projects

### SmartClicker (C#, MIT) -- Most Relevant
- **URL:** https://github.com/foxmadnes/SmartClicker
- **Language:** C# (100%), Windows Forms
- **License:** MIT
- **Status:** Dormant (targets Windows 7)
- **Features:** Dwell clicking for HeadMouse/alternative input, Win32 interop for
  input simulation (Win32Stuff.cs), accessibility-focused
- **Assessment:** Only C# dwell clicker found. Student project, needs heavy
  modernization. Win32 interop patterns and dwell detection logic are reusable.

### KMouseTool (KDE, C++) -- Best Feature Design
- **URL:** https://github.com/KDE/kmousetool
- **License:** KDE licenses
- **Stars:** 13 | Active (711 commits)
- **Features:** Dwell timing, Smart Drag, stroke recognition (rightward = right-click,
  leftward = double-click), audible feedback, session integration
- **Assessment:** Gold standard feature list. Stroke recognition is innovative.

### Tracky Mouse (JS, MIT) -- Best UX Patterns
- **URL:** https://github.com/1j01/tracky-mouse
- **License:** MIT | Stars: 56 | Active (Dec 2024)
- **Features:** Dwell click with shrinking-circle visual feedback, blink-to-click,
  mouth-to-click, separates movement from click overlay
- **Assessment:** Best visual feedback overlay design. Algorithms adaptable to C#.

### Enable Viacam (C++, GPL-3.0)
- **URL:** https://github.com/cmauri/eviacam
- **Stars:** 169 | Mature/maintenance mode
- **Features:** Webcam head tracking + built-in dwell click
- **Assessment:** Good reference for dwell algorithms and config UI. Not C#.

### MouseTool (C++, GPL-2.0)
- **URL:** https://github.com/personalities/mousetool
- **Features:** Smart Drag mode, context-aware click type selection
- **Assessment:** Legacy Win32 code. Smart Drag concept worth studying.

### rtmouse (C, GPL-2.0)
- **URL:** https://github.com/faithanalog/rtmouse
- **Stars:** 12 | Linux/X11
- **Features:** Minimal dwell-click, drag, audio feedback, signal-based control
- **Assessment:** Shows absolute minimum needed for functional dwell clicker.

### GNOME Mousetweaks (C, GPL-3.0)
- **URL:** https://github.com/dr-ni/mousetweaks (fork)
- **Features:** "Hover Click" (dwell), "Simulated Secondary Click" (hold primary
  for right-click), GNOME Control Center integration
- **Assessment:** Model for OS integration and UX design.

## C# Auto-Clicker Projects (Architectural Reference)

### oriash93/AutoClicker (C# WPF, MIT)
- **URL:** https://github.com/oriash93/AutoClicker
- **Stars:** 463 | Active (Aug 2024)
- **Features:** Click intervals, all buttons, coordinate targeting
- **Assessment:** Good WPF UI patterns and click simulation code.

### SpeedAutoClicker (C# .NET 8 WPF, MIT)
- **URL:** https://github.com/rvb3m/SpeedAutoClicker
- **Active:** 2025
- **Features:** Modern dark UI, global hotkeys, .NET 8
- **Assessment:** Most modern C# auto-clicker. Good .NET 8 project structure reference.

### Clicknado (C#, MIT)
- **URL:** https://github.com/DavidRieman/Clicknado
- **Features:** System tray app, low-level keyboard hooks, click simulation
- **Assessment:** Good reference for tray + hooks + click simulation infrastructure.

## Recommended C# Libraries

### Mouse/Keyboard Hooks
- **SharpHook** (MIT, .NET 8, active) -- https://github.com/TolikPylypchuk/SharpHook
  Global hooks + event simulation. Cross-platform. RECOMMENDED.
- **GlobalMouseKeyHook** (MIT, 1.1k stars) -- Popular but unmaintained since 2016.

### Input Simulation
- **H.InputSimulator** (MS-PL, .NET 8, active) -- https://github.com/HavenDV/H.InputSimulator
  Modern SendInput wrapper. NativeAOT support. RECOMMENDED.
- **InputSimulatorStandard** (MIT) -- .NET Standard fork of original InputSimulator.

### System Tray
- **H.NotifyIcon** (MIT, .NET 6+, active) -- https://github.com/HavenDV/H.NotifyIcon
  Rich tray icons, context menus, balloon messages. Supports WPF/WinUI/Console.
  For pure Win32: use direct `Shell_NotifyIcon` P/Invoke instead.

### Transparent Overlays
- **ClickableTransparentOverlay** (Apache-2.0, .NET 8) -- DirectX 11 + ImGui.NET
  https://github.com/zaafar/ClickableTransparentOverlay
  Heavy (DirectX) but shows Win32 patterns for click-through transparency.
- For pure Win32: use `CreateWindowEx` with `WS_EX_LAYERED | WS_EX_TRANSPARENT |
  WS_EX_TOPMOST` and GDI+/Direct2D rendering directly.

## Decision: Libraries vs Direct P/Invoke

For a **Pure Win32 + .NET 8** architecture, we should prefer direct P/Invoke over
third-party libraries where practical:

- **Mouse hooks:** Direct `SetWindowsHookEx` P/Invoke (simpler than adding SharpHook
  dependency for a Windows-only app)
- **Input simulation:** Direct `SendInput` P/Invoke (straightforward for our needs)
- **System tray:** Direct `Shell_NotifyIcon` P/Invoke
- **Overlay rendering:** `CreateWindowEx` + GDI+ (`System.Drawing`) or Direct2D

This keeps dependencies minimal and the footprint tiny.
