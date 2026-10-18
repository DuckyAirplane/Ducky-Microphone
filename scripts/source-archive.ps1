param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files = @()
foreach ($folder in @('Source','scripts','docs','licenses','.github')) {
    $files += Get-ChildItem -LiteralPath (Join-Path $repo $folder) -File -Recurse -Force | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
}
foreach ($name in @('.gitignore','.gitattributes','.editorconfig','README.md','CHANGELOG.md','THIRD-PARTY-NOTICES.md','Launch DuckyMicrophone.cmd')) {
    $files += Get-Item -LiteralPath (Join-Path $repo $name) -Force
}
$zipPath = Join-Path $dist "DuckyMicrophone-$Version-source.zip"
$stream = [IO.File]::Open($zipPath,[IO.FileMode]::Create)
$zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($repo.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }
Write-Output "Source archive ready: $zipPath"
