# Run after build.ps1 on Windows; records evidence for this exact candidate.
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    [xml]$project = Get-Content 'src/NewsletterCombiner.App/NewsletterCombiner.App.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part release version.' }
    $exe = Get-Item 'dist/GoodSort PDF.exe'
    $metadata = $exe.VersionInfo
    if ($metadata.ProductName -cne 'GoodSort PDF') { throw 'Unexpected executable product name.' }
    if ($metadata.ProductVersion -cne $version) { throw 'Unexpected executable product version.' }
    if ($metadata.FileVersion -cne "$version.0") { throw 'Unexpected executable file version.' }
    $signature = Get-AuthenticodeSignature $exe.FullName
    if ($signature.Status -ne 'NotSigned') { throw 'Expected a fresh unsigned build.' }
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
    if ($env:GITHUB_SHA -and $commit -cne $env:GITHUB_SHA) { throw 'Build commit does not match workflow commit.' }
    $changes = git status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Build modified source files or dependency locks.' }
    $hash = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sdk = (dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the SDK.' }
    $manifest = [ordered]@{
        product = $metadata.ProductName
        version = $version
        fileVersion = $metadata.FileVersion
        file = $exe.Name
        bytes = $exe.Length
        sha256 = $hash
        signature = 'unsigned'
        sourceCommit = $commit
        sdk = $sdk
        runtimeIdentifier = 'win-x64'
        workflowRun = $env:GITHUB_RUN_ID
        workflowAttempt = $env:GITHUB_RUN_ATTEMPT
    }
    New-Item -ItemType Directory -Path test-output -Force | Out-Null
    $manifest | ConvertTo-Json | Set-Content test-output/build-manifest.json -Encoding utf8
    "$hash  $($exe.Name)" | Set-Content test-output/SHA256SUMS.txt -Encoding ascii
    if ($env:GITHUB_STEP_SUMMARY) {
        @(
            '### Unsigned Windows build'
            "Product: GoodSort PDF $version"
            "Source commit: $commit"
            "SHA-256: $hash"
            "SDK: $sdk"
            'Core checks and Windows mode checks passed. Office export and physical printing require separate desktop validation.'
            'This is a build candidate, not a published or signed release.'
        ) | Add-Content $env:GITHUB_STEP_SUMMARY
    }
    $manifest | ConvertTo-Json
} finally {
    Pop-Location
}
