# NaturalPoint compatibility release status

**Candidate built. Native release qualification pending.** The candidate is based on HoverPulse commit `2e760805be181148eb2a8b55151511e98ae76f77`. Self-contained Windows x64 and ARM64 builds succeeded. Their complete publish directories are under `artifacts/release`. SHA-256 for every published file is in `artifacts/verification/publish-manifest.json`.

| Evidence | Result |
|---|---|
| Release test suite | 16 passed, zero failed or skipped. Receipt in `artifacts/verification/isolated.trx`. |
| Detector | All five specification traces, minimum and maximum settings, stationary suppression, axis boundaries and large coordinate deltas passed. 288,000 simulated 100 ms deliveries produced the expected 5,760 clicks. |
| Action delivery | Recording backend verified both dispatch paths, ordering, pacing requests, short-write recovery, reentry suppression and drag ownership. |
| Windows UI | A separate thread attached to a newly created, undisplayed Windows desktop. Real controls and synthetic samples exercised toolbar selection, pause during drag, hands-free resume and drop, options cancel/apply, moving the toolbar and confirmed quit. A companion test window verified registration, acknowledgment and quit notification. Synthetic session messages exercised lock, unlock, resume and display-change handling. |
| Settings | Version-one backup and migration, correct false and zero values, negative coordinates, range validation, future-version rejection and recovery from a corrupt primary passed. |
| Native comparison | On Windows 11 x64 build 26200, the signed reference and candidate each passed the right, default-left, double, drag, pause, resume and drop sequence on both native backends. The receiver saw the same ten button edges at the same coordinates in all four runs. One cycle per configuration establishes ordering and target delivery. |
| Environment and endurance | Actual sleep, lock/unlock, RDP reconnect, multiple monitors/DPI, restricted targets, ARM64 execution and the eight-hour elapsed-time soak remain open. |

The normalized tick trace uses an explicit first sample as its baseline. Runtime startup phase, modal behavior, toolbar geometry, float behavior and companion communication should be checked against the pinned NaturalPoint build in the guest. The candidate uses Windows Forms controls and keeps its own settings. A clean candidate profile starts with the captured reference settings.

## Process lifetime checks

A duplicate candidate exited with status 1 and the original continued on both backends. Orderly exit during a drag delivered exactly one left-up and cleared the Windows left-button state. Forced termination of the original candidate and of the reference during a legacy drag left the button held. The test receiver measured that state before cleaning it up. Use `-CheckOrderlyExit` on the candidate or `-CheckForcedExit` on either product to reproduce these checks in a reserved test login.

## Guest procedure

Use separate clean snapshots of the same disposable, interactive Windows guest. Copy the complete candidate publish directory, the signed reference executable and the two files under `tests/WindowsRelease`. Run the PowerShell test within the guest for the reference and then the candidate, for each backend. Provide the verified guest hostname to `-DisposableGuestName`, `-Product Reference` or `Candidate`, the matching `-Executable` and a fresh `-OutputDirectory`. Add `-Alternate` for SendInput. The runner creates the corresponding test-user profile and refuses to overwrite an existing one. It checks target visibility, records button edges at the receiving window and writes a receipt on completion or failure.

Compare event order, target-relative coordinates and inter-edge intervals in the two event logs. Compare timing distributions across repeated runs at the same DPI, refresh and desktop load. Run the candidate with `-SoakHours 8` in an uninterrupted guest console after differential smoke passes. The accelerated detector test measures logical coverage only. Repeat on supported platforms and retain guest, architecture, DPI, Windows build and profiles alongside receipts.

For recovery qualification, repeat with an already-running instance, modal settings, interrupted write, active drag at orderly exit and forced termination, restricted target, lock/unlock, sleep/wake, RDP reconnect and removal of the toolbar's monitor. Record receiver-side edges, physical key/button state, reachable resume control and any Windows-imposed restrictions. A missed expected edge, extra click, stuck owned button or unreachable resume blocks release.

## Physical Windows test procedure

Reserve an interactive test login with a clean profile. Next to the runner, create a local PowerShell data file named `TestHosts.local.psd1`. Set `PhysicalTestHosts` to an array of reserved computer names and `ProtectedHosts` to an array of machines that must never receive test input. Copy this file with the runner when staging a physical test. Supply the exact reserved hostname to `-TestComputerName` and the full Windows identity to `-DedicatedTestAccount`. The runner checks both before changing settings or positioning the pointer.

All 16 isolated tests also passed in an interactive login on a second Windows PC using an undisplayed desktop and a recording backend. In WinRM Session 0 the toolbar test could not observe input, so native qualification still requires an interactive desktop.

For a standard test login, the administrator can set `TestAccountSids` in the local host file to a hashtable keyed by hostname. Pin the SID of a newly provisioned, separate test user. Keep the host file read-only for that user. The runner checks the Windows token SID and rejects a same-session RDP client or clicker before it moves the pointer. This allows the test account to run without permission to inspect other users' process owners.
