# Publishing on GitHub

## First upload

1. Create an empty repository on GitHub named **DuckyMicrophone**. Do not add a license if you want to keep the current choice.
2. Upload the contents of the prepared **source ZIP** (extract it first), or add this folder using GitHub Desktop. Do not upload `App`, `artifacts`, `dist`, `bin`, or `obj`; `.gitignore` already excludes these for Git clients.
3. Commit and push the source. The Windows workflow builds, tests, and packages the app. The workflow has read-only repository permissions; it does not publish a release automatically.
4. On GitHub, open **Releases → Draft a new release**. Create tag **v1.0.0**, title it **DuckyMicrophone 1.0.0**, and attach `DuckyMicrophone-1.0.0-win-x64.zip` plus its `.sha256` file from `dist`.
5. Use the entry in `CHANGELOG.md` as the description. Publish when ready.

Visitors should download the Windows ZIP from Releases, extract all files, and run `DuckyMicrophone.exe`. The app requires a separately installed virtual cable for microphone routing. Do not attach the source ZIP as the Windows download.

## Later versions

Update the project version and changelog. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 -Version 1.0.1
```

Use the matching `v1.0.1` tag and ZIP/checksum on the new release. GitHub Actions also uploads the Windows ZIP/checksum as a workflow artifact; download and extract the artifact before attaching the inner files to a release.

The packages are unsigned. No signing identity, GitHub account, remote repository, or publishing credential is stored in this project. Only third-party dependency license notices are included; the project's own code has no license file.
