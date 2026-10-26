# Building and releasing VulcansLivingWP

## One version number

`version.props` at the repository root is the only place the product version is written:

```xml
<VulcanVersion>1.3.0</VulcanVersion>
```

`src/FluxWall/FluxWall.csproj` imports it and derives `Version`, `FileVersion`, `AssemblyVersion` and
`InformationalVersion` from it. `AppVersion` reads the assembly metadata at runtime, and
`app.manifest` is generated at build time from `app.manifest.template` (the placeholder `@VERSION@`
becomes `1.3.0.0`) because Windows refuses to load an executable whose manifest has no version.

The Inno Setup script does **not** contain a version string: it reads the file version of the built
`VulcansLivingWP.exe`, so the installer, Add/Remove Programs, the About page and the log always agree.

To release, change `VulcanVersion` and rebuild. Do not label a build `1.3.0` until the features it
claims are in it and the checks below have run.

## Prerequisites

- Windows 10/11 64-bit with a desktop session (the live tests need a real desktop layer).
- .NET SDK 6.0 or newer (`tools\build.ps1` prefers `.toolchain\dotnet\dotnet.exe` when present).
- Inno Setup 6 (`ISCC.exe`). `tools\build.ps1` and the Inno compiler are the only tools required.

## 1. Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1
```

Expected output: `BUILD OK VulcansLivingWP.exe ...` in `src\FluxWall\bin\Release\net48`.

Confirm the metadata:

```powershell
(Get-Item src\FluxWall\bin\Release\net48\VulcansLivingWP.exe).VersionInfo |
    Format-List FileVersion, ProductVersion
```

`FileVersion` must be `1.3.0.0` and `ProductVersion` must start with `1.3.0`.

## 2. Test

Built-in suites (each returns 0 only when everything passed):

```
VulcansLivingWP.exe --layout-test=tmp\layouttest
VulcansLivingWP.exe --update-test=tmp\updatetest
```

End-to-end UI suites (see the README for arguments):

```powershell
tools\wallpaper-activation-test.ps1 -Video <clip>
tools\fullscreen-pause-test.ps1     -Video <clip>
tools\environment-pause-test.ps1    -Video <clip>
tools\preview-play-test.ps1         -Video <clip>
tools\auto-switch-test.ps1          -VideoA <clip> -VideoB <clip>
```

Record the actual numbers. If a check is skipped (for example the two-screen live checks on a
single-monitor machine), say so in the release notes instead of implying it passed.

## 3. Build the installer

```
.toolchain\inno\ISCC.exe installer\setup.iss
```

or, with a normal Inno installation:

```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```

Output: `installer\out\VulcansLivingWP_Setup.exe`. The compile fails if the application has not been
built, because the version comes from the exe.

## 4. Verify install, run and uninstall

Use a scratch directory so a real installation is untouched:

```powershell
$setup = "installer\out\VulcansLivingWP_Setup.exe"
$dir   = "$PWD\tmp\install-check"
Start-Process $setup -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART",
    "/CURRENTUSER","/MERGETASKS=!autostart","/DIR=$dir" -Wait
(Get-Item "$dir\VulcansLivingWP.exe").VersionInfo.FileVersion   # 1.3.0.0
Start-Process "$dir\VulcansLivingWP.exe" -ArgumentList "--applywall=<clip>","--tray"
# wait, then:
Start-Process "$dir\VulcansLivingWP.exe" -ArgumentList "--shutdown" -Wait
Start-Process "$dir\unins000.exe" -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART" -Wait
```

Check that the program files are gone and `%LOCALAPPDATA%\VulcansLivingWP` is still there. A silent
uninstall keeps user data unless `/DELETEUSERDATA=1` is passed; only use that flag when the data
really should go.

`installer\out` and `release\` must not be committed.

## 5. Stage the release folder

```
copy installer\out\VulcansLivingWP_Setup.exe release\
```

Regenerate `release\SHA256SUMS.txt` with the new setup hash, and refresh `release\README.md` so it
names 1.3.0 and links to the release page. Keep the source out of that folder: it belongs in Git.

```powershell
Get-FileHash release\VulcansLivingWP_Setup.exe -Algorithm SHA256
```

## 6. GitHub release (maintainer action)

Only after the checks above and the maintainer's explicit approval:

1. Commit the source changes (version bump, README entry, docs, tests).
2. Tag the commit `v1.3.0` and push the tag.
3. Create a **new** release from that tag. Do not overwrite an existing release.
4. Paste the 1.3.0 section of the README as the notes, and state any unverified items honestly.
5. Attach **only** `VulcansLivingWP_Setup.exe` plus `SHA256SUMS.txt`. GitHub adds the source
   archives itself; do not upload a source zip.
6. Leave "Set as the latest release" checked and do not mark it as a prerelease. The update checker
   reads `tag_name`, ignores prereleases, and compares numerically.

For a release candidate, publish it as a **prerelease** with a tag such as `v1.3.0-rc1`. The checker
will ignore it, which is intentional.

## After the release

The update card in About will report the new version within 12 hours (or immediately with the
**Check for updates** button). Verify that once against the published release — that step cannot be
tested before publishing.
