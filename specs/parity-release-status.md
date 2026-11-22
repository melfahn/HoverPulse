# NaturalPoint compatibility release status

**The eight-hour Windows release gate remains open.** Candidate `7777b33` moves the drag worker round trip off the toolbar thread. The worker checks the Windows left-button state before acknowledging a release. If the pipe fails, the toolbar closes the command stream and waits for that worker to finish before trying its fallback. Pause still preserves a held drag, and Resume retains its target and dwell settings.

The two historical runs establish that the receiver missed the drop deadline. The newer run also establishes that the toolbar expanded again, Windows still held the button when sampled, and a control-text request timed out. Those files end before any pre-cleanup follow-up observation. The original worker protocol could block the toolbar thread for two seconds waiting for an acknowledgment. A deterministic private-desktop test delays the reply on both backends, queries the toolbar during the wait and checks that closing waits for that same release. Both cases failed when the synchronous toolbar wait was restored in a reproduction worktree. A separate worker test simulates an input API reporting success while the button remains down. The historical traces lack the worker and toolbar timestamps needed to establish the cause.

The endurance runner saves the partial cycle, button state, toolbar responsiveness, control text and screenshot before recovery. It holds the failed drop target in place for three more seconds so it can timestamp late native edges separately from any later fixture release. It resumes diagnostic cycles only after retiring the candidate and its worker and verifying the input desktop, button state and a clean receiver click from the exact restarted executable. A missed edge fails qualification even when diagnostics continue through the full elapsed window. The test stops synthetic input if it cannot verify safe recovery.

## Hosted qualification of `7777b33`

| Architecture | Windows build | HoverPulse.exe SHA-256 |
|---|---|---|
| x64 | 26100 | `E526B14A4BCF3D8BF3A9FC7109B2037E6080A34412FAE2B25DFE3ED4CF9AAEFC` |
| ARM64 | 26200 | `B286CBABAB6DAE299A92CEEC5230ABB2FA019AB364D42000EC25D605AB5B901A` |

Both architectures passed 28 isolated tests. Both backends delivered the expected native receiver sequence with the 1.2-second and seven-second drag holds and released the held button on orderly and forced toolbar exit. In the eight normal native drops, the receiver saw left-up 348–434 ms after target placement, and toolbar dispatch completed in 2.8–4.5 ms. The runner verified Windows left-up in each case. [Hosted Windows runs and build manifests](https://github.com/melfahn/HoverPulse/actions/runs/45083233127)

Each architecture also completed a deliberately shortened deadline run with two failed cycles and two verified restarts. The four native left-ups arrived 322–358 ms after the shortened deadline, before recovery began. Each run retained the failed verdict and the original partial cycles. The exact bundles, fixtures and independent receipt audit are staged under `artifacts/verification/drag-release-7777b33`.

## Exact-build qualification in the next reserved Windows window

Use the complete hosted-tested publish directory and its `build.json` for the target architecture. Verify every manifest entry after copying. Stage the complete `tests/WindowsRelease` directory from the same candidate commit, including `DeliveryCycles.ps1`, and provide the separately maintained host and account allowlist described below. Use a fresh output directory and a clean dedicated test profile for each run. Reserve enough time for native preflight and the full unattended eight-hour interval.

Qualify both backends with separate `-CheckOrderlyExit` and `-CheckForcedExit` runs and a `-DragHoldMilliseconds 7000` hold. Repeat the presentation, restored profile, DPI movement, restricted-input, lock/unlock, suspend/resume, reconnect and display-removal checks described below. Keep their receipts separate from endurance. Run the legacy backend overnight because both historical misses used it. Keep the session and its input desktop available for the entire interval.

```powershell
$build = Get-Content -LiteralPath $buildManifest -Raw | ConvertFrom-Json
$exeHash = ($build.Files | Where-Object Name -eq HoverPulse.exe).SHA256
foreach ($file in $build.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $publishDirectory $file.Name)).Hash -ne $file.SHA256) {
        throw "Bundle hash mismatch for $($file.Name)"
    }
}
& (Join-Path $fixtureDirectory 'Test-GuestDelivery.ps1') `
    -TestComputerName $reservedHost -DedicatedTestAccount $reservedIdentity `
    -Product Candidate -Executable (Join-Path $publishDirectory 'HoverPulse.exe') `
    -ExpectedSHA256 $exeHash -OutputDirectory $freshOutputDirectory -TraceDrag -SoakHours 8
```

The receipt must show the exact executable hash, a passed result, a completed soak of at least 28,800 seconds, zero failed cycles and zero restarts. Retain `events.txt`, `receiver-drag.log`, `qpc-frequency.txt`, the toolbar and worker `drag-*.log` files, `resource-samples.csv` and `delivery.json`. A `failure-00001` directory identifies the first failed cycle. Use its partial trace, `late-observation.json` and `recovery.json` to distinguish input delivered late, input released during process retirement and any separately recorded fixture release.

## Previous settings candidate

Build `640722a`, x64 SHA-256 `C1CA014CE3B0DD79F39878F817F9F5D7C3DB7CF5B5BBC983E48D6428D2959EF2`, restored the toolbar position where Setup opened after cancelling a staged Bedroom profile reset. The regression reproduced a 980-pixel jump on the previous build. All 24 isolated Windows tests passed after the fix. Native presentation qualification checks the actual toolbar bounds after Move, Setup, Bedroom profile and Cancel.

The same application source passed on Windows 11 ARM64 (build 26200) and Windows Server x64 (build 26100). Each ran all 24 isolated tests and delivered the expected receiver events on both backends. Forced termination of the toolbar released the held drag and cleared the Windows left-button state in all four native runs. The retained ARM64 executable hash was `0D19D9BAE04795A2FFF7E0C341613575F93F64E30F4CFC0C2A89FE43FC95E726`. The hosted x64 hash was `DEE700B4EB14950F4682551CB2B47AD1939DB2014118BB90AA30E1522B9FB4D9`. [Windows release checks and receipts](https://github.com/melfahn/HoverPulse/actions/runs/45060632776)

## Previous candidate endurance result

Build `caa0e23`, x64 SHA-256 `A2D5942B2926AD4EEABCD9DE30315FC06119951E2BF2A045462BD400A40D404B`, samples the cursor in NaturalPoint's DPI-unaware coordinate space and uses the physical cursor for toolbar targets. All 24 isolated tests passed before the profile-reset regression was added. The Windows x64 and ARM64 publish directories have per-file manifests.

On this exact executable, both backends passed ordinary action delivery, duplicate-instance handling, orderly exit during a held drag, forced termination of the toolbar during a held drag, and a seven-second drag hold followed by pause, resume and drop. Actual Windows controls passed all 48 presentation combinations across 96 and 144 DPI. A newly launched process restored the saved position, size, palette, labels and padding.

A single interactive Windows login exposed two simultaneous RDP displays at different scales. Moving the toolbar changed its measured DPI between 96 and 144, with matching target bounds, paint and anchored Resume. Dropping the display that held the paused toolbar left Resume reachable on the remaining display. The same candidate recovered from real session lock/unlock, authenticated reconnect and S3 suspend/resume. Kernel power events and Windows session notifications were recorded.

With the executable and its positive-control receiver running at verified low integrity, neither backend delivered input to the foreground medium-integrity receiver. Both subsequently delivered exactly one down/up pair to the equal-integrity receiver. Neither displayed a failure cue during the blocked attempt. These observations cover low-to-medium isolation. The test did not grant UIAccess or elevate a test user.

The eight-hour test of `caa0e23` ended after 11,085.8 seconds (3 h 4 min 46 s) using the captured legacy-backend settings. An independent trace audit verified all 8,630 expected button events in the first 863 cycles. In cycle 864 the receiver saw drag-down, the runner dwelled on Pause and Resume, and the expected final left-up was missing. Windows still reported the left button held before cleanup. The pointer was at the expected drop destination and the toolbar had expanded to its full width. Reading its control text timed out. The final cycle's successive target placements were 1.10–1.23 seconds apart. The trace establishes a missed release within the observation interval. Its cause remains open.

The first redesigned executable (`016eece`, x64 SHA-256 `459854BD3A16600AB7715C20533BC58BCFFA6ABA634858F22C44971D51CA989A`) passed 23 isolated tests, the full hands-free presentation sequence at measured 96 and 144 DPI, authenticated reconnect, and both drag-exit checks on both backends. All 48 appearance combinations matched their expected target bounds and palette samples. Both presentation runs covered saved and cancelled padding, mode locking, held drag, anchored Resume, Move/Place and confirmed Exit, with zero stray receiver clicks.

At 150% scale that build disagreed with the signed reference on 14 of 30 controlled physical pointer moves. The reference rearmed on a step of 5 in its own Windows coordinate space. The 30 measurements included movement on both axes from three raster origins. An intermediate build (`8eb2b42`) selected a neighboring toolbar button at 150% and was withdrawn. The corrected build matched all 30 reference decisions and all receiver coordinates across 90 target placements. In the 70 paired left-click observations, median latency was 408.5 ms on the reference and 408.7 ms on the candidate. The corresponding 95th percentiles were 455.8 and 456.6 ms.

The previous x64 build (`3F78BA7A…6455`) completed 336 full native endurance cycles before the receiver missed the drop after Resume in the next cycle. The run ended after 4,334 seconds. The last trace shows a drag-down and then a 6.4-second interval before the pointer moved to Pause. The cause is unresolved. This was a failed endurance run. Subsequent qualification captures the toolbar, pointer and Windows button state before cleanup when any phase fails.

## Earlier baseline checks

| Evidence | Result |
|---|---|
| Release test suite | 17 passed, zero failed or skipped after adding the drag ownership regression. The baseline candidate passed 16 tests. |
| Detector | All five specification traces, minimum and maximum settings, stationary suppression, axis boundaries and large coordinate deltas passed. 288,000 simulated 100 ms deliveries produced the expected 5,760 clicks. |
| Action delivery | Recording backend verified both dispatch paths, ordering, pacing requests, short-write recovery, reentry suppression and drag ownership. |
| Windows UI | A separate thread attached to a newly created, undisplayed Windows desktop. Real controls and synthetic samples exercised toolbar selection, pause during drag, hands-free resume and drop, options cancel/apply, moving the toolbar and confirmed quit. A companion test window verified registration, acknowledgment and quit notification. Synthetic session messages exercised lock, unlock, resume and display-change handling. |
| Settings | Version-one backup and migration, correct false and zero values, negative coordinates, range validation, future-version rejection and recovery from a corrupt primary passed. |
| Native comparison | On Windows 11 x64 build 26200, the signed reference and candidate each passed the right, default-left, double, drag, pause, resume and drop sequence on both native backends. The receiver saw the same ten button edges at the same coordinates in all four runs. One cycle per configuration establishes ordering and target delivery. |
| Environment and endurance | This earlier baseline did not complete endurance. The exact candidate results above identify display, sleep, lock, reconnect, restricted-input and native ARM64 coverage. |

The normalized tick trace uses an explicit first sample as its baseline. Runtime timing, modal behavior, float behavior and companion communication can be compared against the pinned NaturalPoint build in the reserved login. The candidate uses Windows Forms controls and keeps its own settings. A clean candidate profile starts with the captured reference settings.

## Process lifetime checks

A duplicate candidate exited with status 1 and the original continued on both backends. Orderly exit during a drag delivered exactly one left-up and cleared the Windows left-button state. Forced termination of the original candidate and of the reference during a legacy drag left the button held. The test receiver measured that state before cleaning it up. HoverPulse now routes only the held drag through a separate process that owns the down and up events. When the toolbar process dies, Windows closes its pipe and the drag process releases the button. The repaired candidate passed actual forced toolbar termination on both native backends. Simultaneous termination of both processes, desktop loss and restricted input targets require separate qualification. Use `-CheckOrderlyExit` on the candidate or `-CheckForcedExit` on either product to reproduce these checks in a reserved test login.

## Native x64 results

The published x64 candidate with SHA-256 `3F78BA7A2DB0F0516F41063357CF8C02572707DEC4727FE01A3A8EBB364A5455` passed the native click sequence, duplicate launch, orderly exit with a held drag and forced toolbar termination on both backends. For forced termination the receiver required exactly one up and checked the Windows button state before any fixture cleanup. These tests terminate the toolbar PID while the drag worker can continue.

A separate run paused the candidate, disconnected its RDP client, observed the test login change to Disconnected, and reconnected to the same Windows session and toolbar PID at a different desktop width. The receiver observed zero edges while paused and one left click after hands-free resume. Use `-CheckReconnect` to run this test. After the runner writes `ready-for-reconnect.json`, have the external coordinator verify disconnection, reconnection and the process identity before it creates the `reconnected` file in the same run directory.

In one matched four-run comparison the first edge reached the receiver 386–450 ms after pointer placement for the reference and 361–803 ms for the candidate. The longest candidate observation was a drag start on the alternate backend. Within an ordinary or double click the observed legacy inter-edge intervals were below 1 ms. Alternate intervals ranged from 109–110 ms on the reference and 109–111 ms on the candidate, rounded to whole milliseconds. These are receiver timings from a small sample on one machine. Timing-distribution equivalence remains open.

## Guest procedure

Use separate clean snapshots of the same disposable, interactive Windows guest. Copy the complete candidate publish directory, the signed reference executable and the receiver and PowerShell scripts under `tests/WindowsRelease`. Run the PowerShell test within the guest for the reference and then the candidate, for each backend. Provide the verified guest hostname to `-DisposableGuestName`, `-Product Reference` or `Candidate`, the matching `-Executable` and a fresh `-OutputDirectory`. Add `-Alternate` for SendInput. The runner creates the corresponding test-user profile and refuses to overwrite an existing one. It checks target visibility, records button edges at the receiving window and writes a receipt on completion or failure.

Compare event order, target-relative coordinates and inter-edge intervals in the two event logs. Compare timing distributions across repeated runs at the same DPI, refresh and desktop load. Run the candidate with `-SoakHours 8`, `-ExpectedSHA256` and `-TraceDrag` in an uninterrupted guest console after differential smoke passes. The accelerated detector test measures logical coverage only. Repeat on supported platforms and retain guest, architecture, DPI, Windows build and profiles alongside receipts.

For recovery qualification, repeat with an already-running instance, modal settings, interrupted write, active drag at orderly exit and forced termination, restricted target, lock/unlock, sleep/wake, RDP reconnect and removal of the toolbar's monitor. Record receiver-side edges, physical key/button state, reachable resume control and any Windows-imposed restrictions. A missed expected edge, extra click, stuck owned button or unreachable resume blocks release.

## Physical Windows test procedure

Reserve an interactive test login with a clean profile. Next to the runner, create a local PowerShell data file named `TestHosts.local.psd1`. Set `PhysicalTestHosts` to an array of reserved computer names and `ProtectedHosts` to an array of machines that must never receive test input. Copy this file with the runner when staging a physical test. Supply the exact reserved hostname to `-TestComputerName` and the full Windows identity to `-DedicatedTestAccount`. The runner checks both before changing settings or positioning the pointer.

All 16 isolated tests also passed in an interactive login on a second Windows PC using an undisplayed desktop and a recording backend. In WinRM Session 0 the toolbar test could not observe input, so native qualification still requires an interactive desktop.

For a standard test login, the administrator can set `TestAccountSids` in the local host file to a hashtable keyed by hostname. Pin the SID of a newly provisioned, separate test user. Keep the host file read-only for that user. The runner checks the Windows token SID and rejects a same-session RDP client or clicker before it moves the pointer. This allows the test account to run without permission to inspect other users' process owners.
