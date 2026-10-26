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

### Display changes exercised on real hardware (2026-10-26)

Span mode was left running on the two physical screens while Windows' display configuration was
changed underneath it. Evidence is taken from the application log and from the monitor rectangles
after each step.

**Disconnect and reconnect** — `DisplaySwitch.exe /internal` (PC screen only) removes the second
display exactly as an unplug does at the API level; `/extend` brings it back.

```
14:13:04  WM_DISPLAYCHANGE received
14:13:07  Rebuilding the wallpaper after a display/shell change (layoutChanged=True, surfaces=1)
14:13:08  Wallpaper set: Span: [span (1920x1080 at 0,0) -> glitch-girl...] on 1 surface(s).
14:13:25  WM_DISPLAYCHANGE received
14:13:29  Rebuilding the wallpaper after a display/shell change (layoutChanged=True, surfaces=1)
14:13:31  Wallpaper set: Span: [span (3840x1080 at -1920,0) -> glitch-girl...] on 1 surface(s).
```

The span shrank to the remaining screen and grew back to the full union; no crash, no stuck black
surface, one decoder throughout.

**Primary-monitor change** — positions were swapped with `ChangeDisplaySettingsEx`
(`CDS_SET_PRIMARY`, `DM_POSITION`) so the left screen became primary, then the original primary was
restored. Resolution and refresh rate were not touched.

```
14:14:19  WM_DISPLAYCHANGE received
14:14:21  Wallpaper set: Span: [span (3840x1080 at 0,0) -> glitch-girl...] on 1 surface(s).
14:14:41  WM_DISPLAYCHANGE received (original primary restored)
14:14:44  Wallpaper set: Span: [span (3840x1080 at -1920,0) -> glitch-girl...] on 1 surface(s).
```

The span followed the new origin in both directions instead of keeping stale coordinates.

**Explorer restart** — `explorer.exe` was terminated; Windows restarted it on its own.

```
14:15:11  Explorer restart detected (TaskbarCreated); wallpaper will be re-applied.
14:15:12  Rebuilding the wallpaper after a display/shell change (hostGone=True, surfaces=1)
14:15:12  Wallpaper layer created: WorkerW 0x53065A visible=True rect=-1920,0 3840x1080.
14:15:13  Wallpaper set: Span: [span (3840x1080 at -1920,0) -> glitch-girl...] on 1 surface(s).
```

A fresh `WorkerW` was found and the span was re-applied about 1.6 seconds after the shell came back;
the application kept running.

**Resolution change** — the primary screen was switched from 1920×1080 to 1280×1024 (refresh rate
kept at 120 Hz) with `ChangeDisplaySettingsEx`, then restored:

```
14:20:01  Display layout changed; re-applying wallpaper.
            was=[Span#span=...@-1920,0,1920,1080|] now=[Span#span=...@-1920,0,1280,1080|]
14:20:02  Wallpaper parented into desktop host before playback (... rect=-1920,0 3200x1080).
14:20:02  Wallpaper loading ... 3200x1080 at -1920,0; the previous wallpaper stays until the first frame.
14:20:03  Media opened: 1920x1080 on Span.
14:20:03  Wallpaper set: Span: [span (3200x1080 at -1920,0) -> glitch-girl...] on 1 surface(s).

14:20:31  Display layout changed; re-applying wallpaper.
            was=[...1280,1080|] now=[...1920,1080|]
14:20:32  Media opened: 1920x1080 on Span.
14:20:32  Wallpaper set: Span: [span (3840x1080 at -1920,0) -> glitch-girl...] on 1 surface(s).
```

The span followed the new union in both directions within about a second, the old wallpaper stayed
up until the replacement decoded its first frame, and the process kept running with one surface
throughout. The 120 Hz refresh rate was preserved and the desktop returned to 1920×1080.

After all the tests the original primary monitor, arrangement, resolution, settings and wallpaper
were restored and verified.

### Independent mode on real hardware (2026-10-26)

Independent mode was left running with a different video on each screen — `white-wolf-girl` on the
left screen and `glitch-girl` on the primary (right) screen — while the display configuration was
changed underneath it. Two separate surface windows were present before and after every step:

```
0x1A6080E  pid=6592  rect=-1920,0 1920x1080  visible=True   white-wolf-girl
0x1307FE   pid=6592  rect=0,0     1920x1080  visible=True   glitch-girl
```

A capture of the whole desktop showed both videos playing at the same time; neither was stretched
across the seam.

**Disconnect and reconnect** — `DisplaySwitch.exe /internal` (PC screen only), then `/extend`:

```
15:12:31  Display layout changed; re-applying wallpaper.
            was=[Independent#<TCL>=white-wolf-girl@-1920,0,0,1080|<BNQ>=glitch-girl@0,0,1920,1080|]
            now=[Independent#<BNQ>=glitch-girl@0,0,1920,1080|]
15:12:31  Wallpaper set: Independent: [<BNQ> (1920x1080 at 0,0) -> glitch-girl...] on 1 surface(s).

15:12:54  Display layout changed; re-applying wallpaper.
            now=[Independent#<TCL>=white-wolf-girl@-1920,0,0,1080|<BNQ>=glitch-girl@0,0,1920,1080|]
15:12:55  Wallpaper set: Independent: [<TCL> (1920x1080 at -1920,0) -> white-wolf-girl...]
                                         [<BNQ> (1920x1080 at 0,0) -> glitch-girl...] on 2 surface(s).
```

The surface on the removed screen was disposed, and the screen that stayed kept **its own** video
rather than falling back to the other one. When the screen returned, both assignments came back from
the saved monitor keys even though Windows had renumbered the GDI device (`\\.\DISPLAY3` became
`\\.\DISPLAY4` and later returned) — the PnP interface path inside the key absorbed the renumbering.

On the reconnect rebuild both new surfaces were revealed by the 700 ms first-frame fallback
(`Revealing the wallpaper surface before its first frame ... decoder did not start on a hidden
window`) and each decoder opened 0.15–0.3 s later. The wallpaper was visible throughout and the log
records the fallback as a warning, not an error; this was seen once, on that rebuild.

**Resolution change** — the primary screen went from 1920×1080 to 1280×1024 at the same 120 Hz,
then back:

```
15:14:02  Display layout changed; re-applying wallpaper.
            now=[Independent#<TCL>=white-wolf-girl@-1920,0,0,1080|<BNQ>=glitch-girl@0,0,1280,1024|]
15:14:03  Media opened: 1920x1080 on \\.\DISPLAY4.
15:14:03  Media opened: 1920x1080 on \\.\DISPLAY3.
15:14:03  Wallpaper set: Independent: [<TCL> (1920x1080 at -1920,0) -> white-wolf-girl...]
                                         [<BNQ> (1280x1024 at 0,0) -> glitch-girl...] on 2 surface(s).

15:14:59  Display layout changed; re-applying wallpaper.   (resolution restored)
15:15:01  Wallpaper set: Independent: [<TCL> (1920x1080 at -1920,0) -> white-wolf-girl...]
                                         [<BNQ> (1920x1080 at 0,0) -> glitch-girl...] on 2 surface(s).
```

Only the affected surface changed size; the other screen's surface kept its rectangle and its
decoder. The desktop union went 3840×1080 → 3200×1080 → 3840×1080, both assignments survived every
step, the commit landed 1.7–1.9 s after the display change, and the 120 Hz mode was preserved in both
directions.

No code change was needed for either scenario. Afterwards the original display mode, resolution,
settings and the installed 1.2.3 build were restored and verified.

### Not verified on hardware

These need equipment or conditions the development machine did not have. They are **not** passes:

- three or more physical monitors (the synthetic plan tests cover the geometry only);
- mixed-DPI scaling between monitors;
- pulling a monitor cable, which also removes the monitor's EDID identity; the disconnect test used
  Windows' own display-mode switch, so the identity-removal path itself was not exercised;
- a full Windows restart with span mode active (application restart with saved assignments is
  covered by the settings reload, and the 1.2.x startup restore path was verified separately);
- Remote Desktop and sleep/resume with the new modes (the pause plumbing is unchanged, and lock /
  display-off pausing was verified separately — see the README test suites).

A single-monitor machine runs the pure and migration tests and reports the two-screen live checks as
`SKIP`; that is deliberate, so a green run never implies untested hardware.
