# Requires desktop Word for the generated RTF conversion check.
param(
    [string]$Executable = (Join-Path $PSScriptRoot '../dist/GoodSort PDF.exe'),
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('GoodSort-Pages-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path $Executable).Path
if ((Test-Path $WorkDirectory) -and @(Get-ChildItem $WorkDirectory -Force).Count -gt 0) { throw 'Use an empty test folder.' }
$arguments = @('--page-smoke', ('"' + $WorkDirectory + '"'))
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru
if (-not $process.WaitForExit(300000)) { $process.Kill(); throw 'Windows page checks timed out.' }
$errorFile = Join-Path $WorkDirectory 'error.txt'
if (Test-Path $errorFile) { throw (Get-Content $errorFile -Raw) }
$results = Join-Path $WorkDirectory 'results.json'
if ($process.ExitCode -ne 0 -or -not (Test-Path $results)) { throw 'Windows page checks did not finish successfully.' }
Get-Content $results
Write-Host "Generated PDFs and interface previews: $WorkDirectory"
