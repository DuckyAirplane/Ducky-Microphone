# Building DuckyMicrophone

Use Windows and a .NET 9-compatible SDK. The SDK is only needed to build; released downloads include the runtime.

From the repository root:

```powershell
dotnet build Source/DuckyMicrophone.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 -Version 1.0.0
```

The packaging script restores dependencies, publishes for Windows x64, runs device-independent audio regression checks, includes dependency notices, and creates a ZIP and SHA-256 checksum in `dist/`. It does not start capture or alter Windows audio settings. `artifacts/`, `dist/`, `App/`, and build outputs are excluded from Git.

## Optional local diagnostics

Run these from a local build, not on machines without audio devices:

- `--integration-test`: opens real app/microphone captures with the mixed output muted, checks live additions/removals, and closes them.
- `--ui-test`: checks the source controls; requires available apps, a microphone, and a selectable output.
- `--audio-status`: writes audio endpoint/session levels.
- `--capture-probe`: briefly measures browser signal levels; does not play or save captured audio.
- `--list-sources`: lists local device and process names/IDs.
- `--preview`: renders the UI without starting capture.

These commands write reports beside the executable. Do not upload reports containing your device/process details. Hardware-independent `--self-test` is the only check run in CI. Successful automated checks do not establish listening quality in a particular game or on all hardware.

The existing local launcher opens `App/DuckyMicrophone.exe`. After cloning, build the project or use the GitHub release download; the source repository does not include that binary folder.
