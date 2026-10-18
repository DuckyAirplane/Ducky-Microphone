param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $repo 'dist'
$artifacts = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Force -Path $dist,$artifacts | Out-Null
$stage = Join-Path $artifacts ('package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    & dotnet publish (Join-Path $repo 'Source/DuckyMicrophone.csproj') -c Release -r win-x64 --self-contained true '-p:DebugType=None' '-p:DebugSymbols=false' "-p:Version=$Version" -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $test = Start-Process -FilePath (Join-Path $stage 'DuckyMicrophone.exe') -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
    $report = Join-Path $stage 'self-test.txt'
    if ($test.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report) -or (Get-Content $report -Raw) -notmatch '^PASS:') { throw 'Audio regression checks failed.' }
    Write-Output (Get-Content $report -Raw)
    Remove-Item -LiteralPath $report
    Copy-Item -LiteralPath (Join-Path $repo 'licenses') -Destination (Join-Path $stage 'licenses') -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'README.md'),(Join-Path $repo 'CHANGELOG.md'),(Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination (Join-Path $stage 'docs') -Recurse
    $nugetLine = & dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate restored dependency notices.' }
    $packages = ($nugetLine -replace '^global-packages:\s*','').Trim()
    $deps = Get-Content (Join-Path $stage 'DuckyMicrophone.deps.json') -Raw | ConvertFrom-Json
    foreach ($pack in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $key = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like "runtimepack.$pack/*" })
        if ($key.Count -ne 1) { throw "Cannot identify bundled $pack version." }
        $runtimeVersion = $key[0].Split('/')[-1]
        $packageDir = Join-Path $packages ($pack.ToLowerInvariant() + '/' + $runtimeVersion)
        $notices = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
        if ($notices.Count -eq 0) { throw "Missing license notices for $pack." }
        foreach ($notice in $notices) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $stage ('licenses/' + $pack + '-' + $notice.Name))
        }
    }
    $zip = Join-Path $dist "DuckyMicrophone-$Version-win-x64.zip"
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($zip,[IO.FileMode]::Create)
    $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
            $relative = $file.FullName.Substring($stage.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($zip + '.sha256') -Value "$hash  $([IO.Path]::GetFileName($zip))" -Encoding ascii
    Write-Output "Release ready: $zip"
} finally {
    $resolved = [IO.Path]::GetFullPath($stage)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

