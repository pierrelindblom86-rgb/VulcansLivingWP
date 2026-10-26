# Update checking (1.3.0)

VulcansLivingWP can tell you when a newer stable release is published on GitHub. It never installs
anything by itself.

## Where it lives

| Part | File |
| --- | --- |
| Checker, version comparison, release parsing | `src/FluxWall/Updates/UpdateService.cs` |
| Automated test | `src/FluxWall/Updates/UpdateSelfTest.cs` |
| About page card | `src/FluxWall/UI/AboutPage.cs` |
| Setting | `AppSettings.CheckForUpdates` (Settings → Behaviour) |
| Cached result | `LastUpdateCheckUtc`, `LatestKnownVersion`, `LatestReleaseUrl`, `LatestReleaseNotes` |

## Request

```
GET https://api.github.com/repos/pierrelindblom86-rgb/VulcansLivingWP/releases?per_page=20
Accept: application/vnd.github+json
User-Agent: VulcansLivingWP/<version> (+https://github.com/pierrelindblom86-rgb/VulcansLivingWP/releases)
```

No token is used and no authentication is required — the repository is public. The request goes out
on a thread-pool thread with an 8 second timeout, TLS 1.2 forced for older Windows installs, and the
system proxy honoured. Nothing about the user, the machine or the wallpaper library is sent.

## Rules

- **Drafts and prereleases are ignored.** The checker filters the array itself rather than relying
  on `/latest`, so a prerelease published after a stable one cannot hide it.
- **Numeric comparison.** `SemVersion` parses `1.10.0` > `1.9.9`, accepts a leading `v`, tolerates
  two-component tags, and treats `1.3.0` as newer than `1.3.0-beta.2`. Unparsable tags are skipped.
- **Official links only.** The API URL must be `https` on `api.github.com`; a release's `html_url` is
  only accepted when it is `https`, otherwise it falls back to the releases page. The browser opens
  the link; the app never downloads a file.
- **Rate limit.** Startup checks run at most once every 12 hours (persisted in settings). Manual
  checks are always allowed.
- **Failure is quiet.** Offline, DNS/proxy failure, timeout, HTTP 403/429 rate limiting, 404 and
  malformed JSON all produce a message on the About page and a log line. Nothing throws into the UI
  or the engine, and playback is untouched.
- **Offline display.** The last successful answer is cached, so About can still show "latest stable
  1.3.0" without a network call.
- **No silent install.** There is no download code in the application at all. The user opens the
  release page and runs Setup, which is the ordinary installer that preserves settings and media.

Startup behaviour: an 8-second timer after launch calls `StartupCheck`, which skips the request when
the last check is younger than 12 hours. The About card therefore already has an answer when the
user opens it, and no notification is shown at sign-in.

## Automated test

```
VulcansLivingWP.exe --update-test=<folder>
```

Writes `update-test-report.txt` and exits 0 only when everything passed. It covers:

- 12 version-comparison cases, including multi-digit components, leading `v`, prerelease ordering
  and unparsable input;
- URL allow-listing (http, look-alike host, `file:`);
- parser fixtures: draft, prerelease, unsorted array, unparsable tag, insecure `html_url`;
- malformed JSON and empty array;
- cached result states, dismissal persistence, disabled setting, settings round trip;
- a **real** HTTPS call to the public API (offline builds of the test would fail this step honestly);
- the offline path, by pointing the system proxy at a closed loopback port.

### Result on the development machine (2026-10-26)

```
Passed: 36   Failed: 0   ALL CHECKS PASSED
```

The live call reported `UpToDate`, latest stable `1.2.3`, installed `1.3.0`; the offline call
reported `Failed` with "You appear to be offline, or GitHub is unreachable."

### Deliberately not claimed

- HTTP 403/429 rate limiting was **not** reproduced against GitHub; the branch is covered by code
  inspection only, and the same handler path is exercised by other HTTP failures.
- A malformed response from the real server cannot be induced; the parser is tested directly with
  bad input instead.
- The "newer prerelease exists" case is covered by parser fixtures (prerelease excluded), not by
  publishing a real prerelease.
- The release page button was not clicked in the automated run; it opens a plain https GitHub URL
  through the shell.

## Release page setup (for the maintainer)

The checker needs nothing configured beyond a published, non-draft, non-prerelease GitHub release
whose tag looks like `v1.3.0`. See `docs/RELEASING.md`.
