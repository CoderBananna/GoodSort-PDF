$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $buildOptions = @()
    if ($env:GITHUB_ACTIONS -eq 'true') { $buildOptions += '-p:ContinuousIntegrationBuild=true' }
    dotnet restore tests/NewsletterCombiner.Tests --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Test dependency restore failed.' }
    dotnet run --project tests/NewsletterCombiner.Tests -c Release --no-restore @buildOptions
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet restore src/NewsletterCombiner.App/NewsletterCombiner.App.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Application dependency restore failed.' }
    dotnet publish src/NewsletterCombiner.App/NewsletterCombiner.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o dist @buildOptions
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $exe = Join-Path $PSScriptRoot 'dist/GoodSort PDF.exe'
    if (-not (Test-Path $exe)) { throw 'The executable was not produced.' }
    $files = @(Get-ChildItem (Join-Path $PSScriptRoot 'dist') -File)
    if ($files.Count -ne 1) { throw 'Expected exactly one executable in dist.' }
    Write-Host "Built: $exe"
    Get-FileHash $exe -Algorithm SHA256
} finally {
    Pop-Location
}
