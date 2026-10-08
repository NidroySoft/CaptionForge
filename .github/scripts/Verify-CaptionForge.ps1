[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# Every invocation uses the repository root; both CI and release share this validation.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repoRoot
try {
    $testProject = 'tests/CaptionForge.Tests/CaptionForge.Tests.csproj'
    $desktopProject = 'src/CaptionForge.Desktop/CaptionForge.Desktop.csproj'
    if (!(Test-Path $testProject) -or !(Test-Path $desktopProject)) {
        throw 'No se encuentran los proyectos Tests y Desktop en sus rutas habituales.'
    }
    $env:CAPTIONFORGE_TEST_FIXTURES = Join-Path $repoRoot 'tests/CaptionForge.Tests/Fixtures'
    if (!(Test-Path (Join-Path $env:CAPTIONFORGE_TEST_FIXTURES 'expected_captions_v3.json'))) {
        throw 'Faltan los Fixtures de xUnit en el repositorio. Añade tests/CaptionForge.Tests/Fixtures a Git.'
    }
    # Hosted CI has no personal media, model, or live CapCut project. Attribute-based skips remain explicit.
    Remove-Item Env:CAPTIONFORGE_TEST_MODEL -ErrorAction SilentlyContinue
    Remove-Item Env:CAPTIONFORGE_TEST_VOICE -ErrorAction SilentlyContinue
    Remove-Item Env:CAPTIONFORGE_TEST_CAPCUT_PROJECT -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Path 'artifacts/test-results' -Force | Out-Null
    & dotnet restore $desktopProject
    if ($LASTEXITCODE -ne 0) { throw 'Falló restore del Desktop.' }
    & dotnet restore $testProject
    if ($LASTEXITCODE -ne 0) { throw 'Falló restore de Tests.' }
    & dotnet build $desktopProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del Desktop.' }
    & dotnet test $testProject --configuration Release --no-restore --logger 'trx;LogFileName=CaptionForge.Tests.trx' --results-directory 'artifacts/test-results' --collect 'XPlat Code Coverage'
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas xUnit.' }

    $desktopDirectory = Join-Path $repoRoot 'src/CaptionForge.Desktop'
    $desktopAssembly = Join-Path $desktopDirectory 'bin/Release/net10.0-windows/CaptionForge.dll'
    $appearanceChecks = 'checks/CaptionForge.AppearanceChecks/CaptionForge.AppearanceChecks.csproj'
    $pass2Checks = 'checks/CaptionForge.Pass2Checks/CaptionForge.Pass2Checks.csproj'
    if (Test-Path $appearanceChecks) {
        & dotnet run --project $appearanceChecks --configuration Release -- (Join-Path $desktopDirectory 'Resources/Palettes.json') $desktopAssembly
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las comprobaciones de apariencia.' }
    }
    if (Test-Path $pass2Checks) {
        & dotnet run --project $pass2Checks --configuration Release -- $desktopDirectory $desktopAssembly
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las comprobaciones del pase 2.' }
    }
    $moduleChecks = 'checks/CaptionForge.ModuleChecks/CaptionForge.ModuleChecks.csproj'
    if (Test-Path $moduleChecks) {
        & dotnet run --project $moduleChecks --configuration Release -- 'artifacts/module-checks'
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las comprobaciones de las vistas y navegación de módulos.' }
    }
}
finally { Pop-Location }
