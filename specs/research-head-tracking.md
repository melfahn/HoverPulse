# Head-Tracking Devices and the Dwell Clicker Relationship

**Date:** 2026-02-20

## The Key Insight

A dwell clicker is SEPARATE from the pointing device:
- **Pointing device** moves the cursor (head tracker, joystick, eye tracker, etc.)
- **Dwell clicker** watches cursor position and triggers clicks when stationary

This separation matters because:
1. **Device independence** — one dwell clicker works with ANY pointing device
2. **User choice** — mix and match what works for each individual's body
3. **Resilience** — when SmartNav was discontinued, users lost both pointer AND clicker
4. **Quality** — dedicated tools do one thing well

## SmartNav by NaturalPoint

### What It Was
- IR camera tracked a reflective dot on forehead/glasses
- Mapped head movement to cursor movement with high precision
- SmartNav 4 AT included: built-in dwell clicking, Click-N-Type virtual keyboard,
  voice clicking, switch input jacks
- Complete turnkey hands-free computing solution

### Discontinuation
- Discontinued after ~20 years of production
- No replacement planned
- NaturalPoint pivoted to OptiTrack (motion capture) and TrackIR (gaming)
- Devastating for accessibility community — users who relied on it for 17+ years
  left stranded
- NaturalPoint continues selling TrackIR (same IR tech) but chose not to add
  accessibility features to it

## QUHA Zono

- Gyroscopic Bluetooth mouse worn on head/glasses
- Detects rotational movement via internal gyroscope
- No line-of-sight required (unlike IR trackers)
- Works with any Bluetooth device
- Zono X adds gesture recognition
- Price: ~$650+ USD
- **Has NO built-in clicking** — explicitly tells users to use Dwell Clicker 2
  or other third-party dwell software

## Camera-Based Head Tracking (No Hardware)

### Enable Viacam (eViacam)
- Free, open-source (GPL v3), webcam-based
- Tracks facial features via computer vision
- Includes built-in dwell clicking
- Available for Windows and Linux
- Older CV techniques, accuracy depends on lighting

### Google Project Gameface
- Open-source by Google, uses MediaPipe ML models
- Tracks head movements and facial gestures via webcam
- Eyebrow raise to click, mouth open to move cursor
- Available for PC and Android

### Tracky Mouse
- Open-source MIT, Electron-based
- Head tracking + dwell-to-click + blink-to-click + mouth-to-click
- Modern alternative to eViacam

### Other
- FaceTrackNoIR / OpenTrack — primarily for gaming, can do mouse emulation
- CameraMouse AI — academic project from Boston University

## TrackIR (NaturalPoint Gaming Product)

- Same core IR tracking tech as SmartNav
- Mouse emulation exists but is very limited (only works when TrackIR is focused)
- Not compatible with dwell click or switches out of the box
- Can be hacked via FreePIE but requires technical setup
- Community bitter that NaturalPoint sells gaming product but killed accessibility one

## Eye Trackers

- **Tobii Dynavox PCEye:** ~$6,900, bundled with TD Control dwell software
- **Tobii Eye Tracker 5:** ~$200, gaming tier, no dwell click software
- Massive price gap ($200 vs $7,000) between gaming and accessibility tiers

## The Market Gap

- SmartNav discontinued, no replacement
- Windows has NO built-in dwell click (macOS does)
- QUHA Zono users need third-party dwell software
- Gaming eye tracker users have no click solution
- Free options are dated, basic, or bundled with their own head tracking
- **Clear need for a free, open-source, device-independent dwell clicker for Windows**
