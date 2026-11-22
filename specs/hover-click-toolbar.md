# Hover Click toolbar

The approved toolbar keeps the Pause/Resume, Left, Double, Drag, Right, Setup, Exit, Move/Place order. It defaults to black and gray with icons only. Light and blue dark use the same targets. Setup stages appearance, size, padding and labels until OK. Settings remain in the existing HoverPulse profile.

| Size | Saved AppSize | Regular target (DIP) | Pause/Resume width (DIP) | Gap (DIP) |
|---|---:|---:|---:|---:|
| Extra Small | 0 | 44 × 52 | 56 | 5 |
| Small | 1 | 48 × 62 | 64 | 6 |
| Medium | 2 | 56 × 68 | 72 | 8 |
| Large | 3 | 64 × 76 | 80 | 10 |

The 1 DIP shell border surrounds adjustable padding, initially 4 DIP. The implementation supports 0–24 DIP in one-DIP steps. Existing valid sizes 1–3 keep their relative size. Target edges are calculated in logical units and rounded separately at the current DPI. The shell and targets have 5 DIP and 3 DIP corner radii. The divider occupies the gap before Setup.

A solid outline and padlock identify the locked action. A dashed outline and arrow mark a one-use action. A held drag inverts the target and adds a link marker. Resume uses the original Pause rectangle. Dwell progress and state marks paint inside existing targets. Toolbar gaps cancel pending dwell progress, and the same movement threshold continues to govern rearming.

The isolated presentation tests exercise real controls for every size, palette and label mode, verify their Windows hit targets, and exercise the setup controls with a simulated head pointer. Exact geometry tests cover 96 and 144 DPI. On a reserved input desktop, run the Windows release fixture with `-CheckDesign -ExpectedDpi 96` or `144`. It measures the actual window DPI, exercises the preference matrix by dwell, compares Windows rectangles and visible colors, and writes screenshots and a JSON receipt. Use a fresh test profile for each run. The pre-redesign endurance result belongs to its own executable hash.
