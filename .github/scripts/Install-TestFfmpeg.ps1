[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (!(Get-Command ffmpeg -ErrorAction SilentlyContinue) -or !(Get-Command ffprobe -ErrorAction SilentlyContinue)) {
    & choco install ffmpeg --yes --no-progress --limit-output
    if ($LASTEXITCODE -notin @(0, 3010)) { throw 'No se pudo instalar FFmpeg para las pruebas.' }
    $packageDirectory = Join-Path $env:ChocolateyInstall 'lib/ffmpeg'
    $executable = Get-ChildItem $packageDirectory -Recurse -Filter 'ffmpeg.exe' -File | Select-Object -First 1
    if ($null -eq $executable) { throw 'FFmpeg instalado, pero no se encuentra ffmpeg.exe.' }
    $toolsDirectory = $executable.DirectoryName
    $env:PATH = "$toolsDirectory;$env:PATH"
    if ($env:GITHUB_PATH) { $toolsDirectory | Out-File -FilePath $env:GITHUB_PATH -Encoding utf8 -Append }
}
& ffmpeg -version
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg no se ejecuta correctamente.' }
& ffprobe -version
if ($LASTEXITCODE -ne 0) { throw 'FFprobe no se ejecuta correctamente.' }
