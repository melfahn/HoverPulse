# HoverPulse Project Memory

## Project Identity
- **Type:** Windows accessibility dwell-click tool (device-independent)
- **Stack:** C# 12+ / .NET 8 / Pure Win32 + GDI+ (NO WPF/WinUI/WinForms)
- **License:** GPL-3.0
- **Owner:** mel (QuadSoft)
- **Repo root:** F:/Documents/SourceCode/QuadSoft/HoverPulse

## Architecture Decision: Pure Win32
- WinUI 3 rejected: transparent click-through overlay conflicts with IsAlwaysOnTop (known bug)
- WPF rejected: user preference for lighter solution
- Pure Win32 chosen: max performance, ~5MB, full control over windowing, UIAccess support

## Key Files
- `CLAUDE.md` — Agent instructions, conventions, build commands
- `specs/requirements.md` — v1.0 feature spec
- `specs/research-*.md` — Dwell clicking, head tracking, codebase survey research
- `src/HoverPulse/Core/DwellDetector.cs` — Core dwell detection engine
- `src/HoverPulse/Core/MouseHook.cs` — WH_MOUSE_LL global hook
- `src/HoverPulse/Core/ClickSimulator.cs` — SendInput click injection
- `src/HoverPulse/Interop/NativeMethods.cs` — All P/Invoke declarations

## Build Quirks
- `LibraryImport` (source-gen) doesn't support structs with `ByValTStr` — use `DllImport` for RegisterClassEx, Shell_NotifyIcon
- `TreatWarningsAsErrors` is on globally via Directory.Build.props
- Test project suppresses CA1707 for underscore test method names
- FluentAssertions v8+ has commercial license warning — may need to switch to Shouldly or plain xUnit asserts

## Conventions
- File-scoped namespaces, nullable enabled
- All Win32 interop centralized in Interop/ folder
- Private fields: `_camelCase`
- Test naming: `Method_Condition_ExpectedResult`
