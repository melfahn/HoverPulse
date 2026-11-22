# NaturalPoint compatibility release status

**The redesigned Hover Click candidate is undergoing native qualification.** Its application source is `016eece107f760ea6e442a857f3ebbdee83cf7aa`. Its self-contained Windows x64 executable has SHA-256 `459854BD3A16600AB7715C20533BC58BCFFA6ABA634858F22C44971D51CA989A`. Both x64 and ARM64 publish directories have individual file manifests. Release results below identify which binary was tested.

The redesigned toolbar passed 23 isolated tests, including real Windows controls for all four sizes, three palettes and both label settings. The tests checked actual control bounds, inert gaps, saved padding, staged Setup and the anchored Resume target. The layout tests also checked 96 and 144 DPI. The earlier native measurements below belong to the previous build.

The exact redesigned x64 executable also passed the full hands-free presentation sequence on Windows 11 at 96 and 144 DPI, independently reported by its live window. All 48 combinations matched the expected physical target coordinates and palette samples. Both runs covered saved and cancelled padding, mode locking, held drag, anchored Resume, Move/Place and confirmed Exit, with zero stray receiver clicks. The x64 candidate also passed an authenticated RDP disconnect and reconnect while paused. Endurance and multi-monitor results are tracked separately.

The previous x64 build (`3F78BA7A…6455`) completed 336 full native endurance cycles before the receiver missed the drop after Resume in the next cycle. The run ended after 4,334 seconds. The last trace shows a drag-down and then a 6.4-second interval before the pointer moved to Pause. The cause is unresolved. This was a failed endurance run. Subsequent qualification captures the toolbar, pointer and Windows button state before cleanup when any phase fails.

| Evidence | Result |
|---|---|
| Release test suite | 17 passed, zero failed or skipped after adding the drag ownership regression. The baseline candidate passed 16 tests. |
| Detector | All five specification traces, minimum and maximum settings, stationary suppression, axis boundaries and large coordinate deltas passed. 288,000 simulated 100 ms deliveries produced the expected 5,760 clicks. |
| Action delivery | Recording backend verified both dispatch paths, ordering, pacing requests, short-write recovery, reentry suppression and drag ownership. |
| Windows UI | A separate thread attached to a newly created, undisplayed Windows desktop. Real controls and synthetic samples exercised toolbar selection, pause during drag, hands-free resume and drop, options cancel/apply, moving the toolbar and confirmed quit. A companion test window verified registration, acknowledgment and quit notification. Synthetic session messages exercised lock, unlock, resume and display-change handling. |
| Settings | Version-one backup and migration, correct false and zero values, negative coordinates, range validation, future-version rejection and recovery from a corrupt primary passed. |
| Native comparison | On Windows 11 x64 build 26200, the signed reference and candidate each passed the right, default-left, double, drag, pause, resume and drop sequence on both native backends. The receiver saw the same ten button edges at the same coordinates in all four runs. One cycle per configuration establishes ordering and target delivery. |
| Environment and endurance | Actual sleep, explicit lock/unlock, multiple monitors/DPI, restricted targets, ARM64 execution and the eight-hour elapsed-time soak remain open. |

The normalized tick trace uses an explicit first sample as its baseline. Runtime startup phase, modal behavior, toolbar geometry, float behavior and companion communication should be checked against the pinned NaturalPoint build in the guest. The candidate uses Windows Forms controls and keeps its own settings. A clean candidate profile starts with the captured reference settings.

## Process lifetime checks

A duplicate candidate exited with status 1 and the original continued on both backends. Orderly exit during a drag delivered exactly one left-up and cleared the Windows left-button state. Forced termination of the original candidate and of the reference during a legacy drag left the button held. The test receiver measured that state before cleaning it up. HoverPulse now routes only the held drag through a separate process that owns the down and up events. When the toolbar process dies, Windows closes its pipe and the drag process releases the button. The repaired candidate passed actual forced toolbar termination on both native backends. Simultaneous termination of both processes, desktop loss and restricted input targets require separate qualification. Use `-CheckOrderlyExit` on the candidate or `-CheckForcedExit` on either product to reproduce these checks in a reserved test login.

## Native x64 results

The published x64 candidate with SHA-256 `3F78BA7A2DB0F0516F41063357CF8C02572707DEC4727FE01A3A8EBB364A5455` passed the native click sequence, duplicate launch, orderly exit with a held drag and forced toolbar termination on both backends. For forced termination the receiver required exactly one up and checked the Windows button state before any fixture cleanup. These tests terminate the toolbar PID while the drag worker can continue.

A separate run paused the candidate, disconnected its RDP client, observed the test login change to Disconnected, and reconnected to the same Windows session and toolbar PID at a different desktop width. The receiver observed zero edges while paused and one left click after hands-free resume. Use `-CheckReconnect` to run this test. After the runner writes `ready-for-reconnect.json`, have the external coordinator verify disconnection, reconnection and the process identity before it creates the `reconnected` file in the same run directory.

In one matched four-run comparison the first edge reached the receiver 386–450 ms after pointer placement for the reference and 361–803 ms for the candidate. The longest candidate observation was a drag start on the alternate backend. Within an ordinary or double click the observed legacy inter-edge intervals were below 1 ms. Alternate intervals ranged from 109–110 ms on the reference and 109–111 ms on the candidate, rounded to whole milliseconds. These are receiver timings from a small sample on one machine. Timing-distribution equivalence remains open.

## Guest procedure

Use separate clean snapshots of the same disposable, interactive Windows guest. Copy the complete candidate publish directory, the signed reference executable and the receiver and PowerShell scripts under `tests/WindowsRelease`. Run the PowerShell test within the guest for the reference and then the candidate, for each backend. Provide the verified guest hostname to `-DisposableGuestName`, `-Product Reference` or `Candidate`, the matching `-Executable` and a fresh `-OutputDirectory`. Add `-Alternate` for SendInput. The runner creates the corresponding test-user profile and refuses to overwrite an existing one. It checks target visibility, records button edges at the receiving window and writes a receipt on completion or failure.

Compare event order, target-relative coordinates and inter-edge intervals in the two event logs. Compare timing distributions across repeated runs at the same DPI, refresh and desktop load. Run the candidate with `-SoakHours 8` in an uninterrupted guest console after differential smoke passes. The accelerated detector test measures logical coverage only. Repeat on supported platforms and retain guest, architecture, DPI, Windows build and profiles alongside receipts.

For recovery qualification, repeat with an already-running instance, modal settings, interrupted write, active drag at orderly exit and forced termination, restricted target, lock/unlock, sleep/wake, RDP reconnect and removal of the toolbar's monitor. Record receiver-side edges, physical key/button state, reachable resume control and any Windows-imposed restrictions. A missed expected edge, extra click, stuck owned button or unreachable resume blocks release.

## Physical Windows test procedure

Reserve an interactive test login with a clean profile. Next to the runner, create a local PowerShell data file named `TestHosts.local.psd1`. Set `PhysicalTestHosts` to an array of reserved computer names and `ProtectedHosts` to an array of machines that must never receive test input. Copy this file with the runner when staging a physical test. Supply the exact reserved hostname to `-TestComputerName` and the full Windows identity to `-DedicatedTestAccount`. The runner checks both before changing settings or positioning the pointer.

All 16 isolated tests also passed in an interactive login on a second Windows PC using an undisplayed desktop and a recording backend. In WinRM Session 0 the toolbar test could not observe input, so native qualification still requires an interactive desktop.

For a standard test login, the administrator can set `TestAccountSids` in the local host file to a hashtable keyed by hostname. Pin the SID of a newly provisioned, separate test user. Keep the host file read-only for that user. The runner checks the Windows token SID and rejects a same-session RDP client or clicker before it moves the pointer. This allows the test account to run without permission to inspect other users' process owners.
