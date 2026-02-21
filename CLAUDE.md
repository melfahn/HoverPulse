# HoverPulse - Claude Code Agent Instructions

## Project Overview
HoverPulse is a free, open-source Windows accessibility tool that triggers mouse
clicks when the cursor remains stationary (dwells) for a configurable duration.
It is device-independent — works with any pointing device (head trackers, eye trackers,
gyroscopic mice, joysticks, standard mice, etc.).

**License:** GPL-3.0

## Tech Stack
- **Language:** C# 12+ (.NET 8)
- **UI Framework:** None (Pure Win32 + GDI+ via P/Invoke)
- **Target OS:** Windows 10 1903+ / Windows 11 only
- **Build System:** MSBuild via `dotnet` CLI
- **Testing:** xUnit + FluentAssertions
- **IDE Config:** .editorconfig enforced

## Architecture: Pure Win32 + .NET 8
No XAML framework (no WPF, no WinUI, no WinForms). All windows are created via
`CreateWindowEx`, rendered with GDI+ (`System.Drawing`) or Direct2D, and managed
through a standard Win32 message loop. All Win32 interop is centralized in the
`Interop/` folder.

### Why Pure Win32
- Transparent click-through overlays work reliably with `WS_EX_LAYERED | WS_EX_TRANSPARENT`
- `uiAccess="true"` manifest works for assistive technology elevation
- Minimal footprint (~5MB, <15MB RAM, sub-500ms startup)
- Total control over windowing behavior critical for accessibility tools

## Project Structure
```
HoverPulse/
├── CLAUDE.md                    # This file
├── .editorconfig                # Code style enforcement
├── .gitignore                   # Git ignore rules
├── Directory.Build.props        # Shared build settings
├── HoverPulse.slnx            # Solution file
├── specs/                       # Requirements and research docs
├── memory/                      # Claude persistent memory
├── src/HoverPulse/
│   ├── HoverPulse.csproj
│   ├── app.manifest             # DPI awareness, UAC settings
│   ├── Program.cs               # Entry point + message loop
│   ├── Core/
│   │   ├── DwellDetector.cs     # Cursor monitoring + dwell timing
│   │   ├── MouseHook.cs         # WH_MOUSE_LL global hook
│   │   ├── ClickSimulator.cs    # SendInput wrapper
│   │   └── HotkeyManager.cs     # RegisterHotKey wrapper
│   ├── Interop/
│   │   ├── NativeMethods.cs     # All P/Invoke declarations
│   │   └── Structs.cs           # Win32 structs (POINT, INPUT, MSG, etc.)
│   ├── Services/
│   │   └── SettingsService.cs   # JSON settings persistence
│   └── UI/                      # (future) Toolbar, overlay, tray, settings windows
└── tests/HoverPulse.Tests/
    └── Core/
        └── DwellDetectorTests.cs
```

## Coding Conventions

### General
- File-scoped namespaces (`namespace Foo;`)
- `var` when type is obvious from RHS
- `readonly` and `const` wherever possible
- Nullable reference types **enabled** project-wide
- No `#region` blocks
- No `this.` prefix unless required

### Naming
- **PascalCase**: types, methods, properties, events, public fields, constants
- **camelCase**: local variables, parameters
- **_camelCase**: private fields (underscore prefix)
- **I-prefix**: interfaces (`IClickStrategy`)
- **Async suffix**: async methods (`LoadSettingsAsync`)
- Test methods use `Method_Condition_ExpectedResult` format (CA1707 suppressed in tests)

### Win32 Interop
- Use `LibraryImport` (source-generated) over `DllImport` where the source generator supports the types
- Fall back to `DllImport` for structs with `ByValTStr` or other unsupported marshalling
- Always specify `SetLastError` and `CharSet`/`StringMarshalling`
- All P/Invoke in `Interop/NativeMethods.cs` — never scattered across classes
- All Win32 structs in `Interop/Structs.cs`
- Window classes: `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST` for overlay
- `WS_EX_TOOLWINDOW | WS_EX_TOPMOST` for toolbar (no taskbar entry)

### Error Handling
- Specific exception types, never catch bare `Exception` unless re-throwing
- `ArgumentNullException.ThrowIfNull()` for public API guards
- Win32 errors: check return values, use `Marshal.GetLastWin32Error()`

## Build & Run Commands
```bash
# Build
dotnet build HoverPulse.slnx

# Run
dotnet run --project src/HoverPulse/HoverPulse.csproj

# Test
dotnet test HoverPulse.slnx

# Publish single-file
dotnet publish src/HoverPulse/HoverPulse.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Key Technical Decisions
- Pure Win32 over WPF/WinUI — WinUI 3 has showstopper bugs with transparent click-through overlays + always-on-top
- Direct P/Invoke over wrapper libraries — fewer dependencies, smaller footprint
- `WH_MOUSE_LL` hook over polling — immediate cursor position updates
- `SendInput` for click simulation — standard Win32 API, works across processes
- JSON settings in `%APPDATA%` — simple, human-readable, no registry pollution
- Single-instance via named mutex

## Things to Watch
- **UIPI:** `SendInput` is silently dropped when targeting elevated windows. Future: `uiAccess="true"` manifest
- **Hook performance:** `WH_MOUSE_LL` callback must return quickly (<200ms or Windows unhooks it)
- **Midas Touch:** Users need ways to rest the cursor without triggering clicks
- **Multi-monitor:** Virtual desktop coordinates may be negative; `SendInput` absolute coords need normalization
- **DPI scaling:** Tolerance radius must account for per-monitor DPI
- **FluentAssertions:** v8+ has commercial license requirements — evaluate alternatives if needed
