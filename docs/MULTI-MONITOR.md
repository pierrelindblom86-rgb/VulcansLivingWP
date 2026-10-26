# Multi-monitor design (1.3.0)

This note explains how the three display modes are built and what was actually verified. It is meant
for anyone maintaining the layout code, not for end users — the README covers everyday use.

## The model: one plan, many surfaces

`Engine/WallpaperPlan.cs` turns *settings + library + monitor list* into the set of **surfaces** that
should be on screen. A surface is one `WallpaperWindow` (one video decoder) plus the rectangle it
covers. Building a plan is pure: no windows are created, nothing is applied.

| Mode | Surfaces produced |
| --- | --- |
| `SameAll` | One per monitor chosen in Settings; every surface plays `ActiveWallpaperId`. |
| `Independent` | One per **connected** monitor; each plays its assignment, or the active wallpaper when unassigned. |
| `Span` | **One** surface whose rectangle is the union of the participating monitors. |

Each plan carries a `Signature` (mode + surface keys + rectangles + item ids). The engine compares
signatures before swapping, which is what makes a repeated Apply or a startup restore racing a
manual pick a no-op, and lets a display change be detected without guessing.

## Monitor identity

`MonitorService` enumerates displays with `EnumDisplayMonitors` and describes each one with
`EnumDisplayDevices(..., EDD_GET_DEVICE_INTERFACE_NAME)`:

- the key is the PnP monitor interface path (`id:\\?\DISPLAY#...`), which survives reboots, cable
  order and monitor re-enumeration;
- when Windows exposes no interface path, the GDI device name (`dev:\\.\DISPLAY1`) is used instead —
  stable across reboots of the same layout, not across port changes.

`MONITOR` handles are re-created by Windows on every display change, so they are never stored or
compared across time. Settings store keys, never handles.

Assignments live in `AppSettings.MonitorAssignments` (key → wallpaper id). They are only removed when
the user presses **Clear**; an unplugged monitor's entry stays and is used again when the key comes
back. If none of the saved span monitors are present, span falls back to all connected monitors and
logs that it did so.

## Coordinates

Rectangles are physical desktop pixels from `MONITORINFO.rcMonitor`, the same coordinate space
Windows uses for its display arrangement. The code therefore handles negative origins (a monitor to
the left of the primary), monitors above the primary, different resolutions and arbitrary
arrangements without special cases: span simply takes the min/max of the participants.

## Rendering

- **Independent** creates one `MediaElement` per monitor. Each window is positioned at its monitor's
  rectangle and attached to the desktop `WorkerW` layer. Decoding is per monitor, which is the only
  way two different videos can play at once.
- **Span** creates one window covering the union and lets the single `MediaElement` render the whole
  frame into it. Continuity is structural: there is one frame and one timeline, so no seam
  synchronisation, no drift, and no second decoder. Stretch/Fit is applied through
  `MediaElement.Stretch` (`SpanFit`), not by cropping per monitor.
- **Same all** is unchanged from 1.2.x: one window per selected monitor, each with the same source.

The 1.2.2 swap rules are kept for every mode: new windows are created hidden, attached, and only
revealed once their decoder reports a frame; the old windows keep playing until then; a swap that
produces no frame within five seconds is cancelled and the previous wallpaper stays.

## Display changes

`WM_DISPLAYCHANGE` and the `TaskbarCreated` broadcast schedule a debounced re-check (800 ms). The
engine rebuilds the plan from the current monitors and compares signatures, so a real change
re-applies while a spurious driver notification does nothing. The five-second health tick remains
the backstop. Span mode picks up a new union rectangle automatically; returned monitors are matched
by key again.

## Settings and migration

`AppSettings.Normalize()` repairs the 1.3 fields and runs the schema migration:

- 1.2.x files (schema 0) keep `ActiveWallpaperId`, monitor choice and scaling, and start in
  `SameAll` — exactly the old behaviour.
- New: `DisplayMode`, `MonitorAssignments`, `SpanMonitors`, `SpanWallpaperId`, `SpanFit`,
  `SettingsSchema`.
- Duplicate or empty assignment keys are dropped on load, so a hand-edited file cannot give one
  monitor two wallpapers.

Users' video files are only ever referenced by path; nothing is moved, renamed or deleted.

## What the automated test proves

`VulcansLivingWP.exe --layout-test=<folder>` (code in `Engine/LayoutSelfTest.cs`) writes
`layout-test-report.txt`. It has three parts:

1. **Synthetic geometry** — three screens including a negative origin and a lower, taller screen;
   checks surface counts, per-monitor mapping, fallback for unassigned screens, preservation of a
   disconnected monitor's assignment, subset spans, a vertical stack, signature stability, empty
   plans for missing wallpaper or no monitors.
2. **Migration** — a real 1.2.3 settings file is loaded through `SettingsStore` and checked for
   SameAll, schema 1, preserved wallpaper and preserved user options.
3. **Live engine** — on the machine's real monitors with real videos: two surfaces with two
   decoders for Independent, correct video per monitor, advancing playback positions, attached and
   visible windows, then a switch to Span (one surface, union rectangle, one decoder, no interval
   with zero surfaces, advancing playback), then back to Independent (assignments restored), then
   SameAll (shared video on every surface), then a settings reload.

### Result on the development machine (2026-10-26)

Two physical monitors, 1920×1080 each, arranged side by side with the left one at x = −1920:

```
Passed: 50   Failed: 0   Skipped: 0
ALL EXECUTABLE CHECKS PASSED
```

Independent mode played two different 1080p videos simultaneously; span mode produced one surface
at `-1920,0 3840x1080`; switching modes kept every assignment; the engine stop removed every surface.

### Not verified on hardware

These need equipment or conditions the development machine did not have. They are **not** passes:

- three or more physical monitors (the synthetic plan tests cover the geometry only);
- mixed-DPI scaling between monitors;
- physically unplugging/replugging a monitor or changing the primary display while playing;
- an Explorer restart and a Windows restart with span mode active;
- Remote Desktop and sleep/resume with the new modes (the pause plumbing is unchanged, and lock /
  display-off pausing was verified separately — see the README test suites).

A single-monitor machine runs the pure and migration tests and reports the two-screen live checks as
`SKIP`; that is deliberate, so a green run never implies untested hardware.
