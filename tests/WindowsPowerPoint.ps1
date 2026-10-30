# Run in a logged-in Windows desktop session with desktop PowerPoint installed.
# Uses generated fixtures only; it never prints or changes the user's documents.
param(
    [string]$Executable = (Join-Path $PSScriptRoot '../dist/GoodSort PDF.exe'),
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('GoodSort-PowerPoint-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path $Executable).Path
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if ((Test-Path $WorkDirectory) -and @(Get-ChildItem $WorkDirectory -Force).Count -gt 0) { throw 'Use an empty test folder.' }
$inputs = Join-Path $WorkDirectory 'inputs'
$exports = Join-Path $WorkDirectory 'exports'
New-Item -ItemType Directory -Path $inputs, $exports -Force | Out-Null

function Release-Com($value) {
    if ($null -ne $value -and [Runtime.InteropServices.Marshal]::IsComObject($value)) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($value)
    }
}

$ppt = $null; $deck = $null; $oldAlerts = $null
$cases = @()
try {
    $ppt = New-Object -ComObject PowerPoint.Application
    $oldAlerts = $ppt.DisplayAlerts
    $ppt.DisplayAlerts = 1
    foreach ($count in @(1, 3)) {
        $deck = $ppt.Presentations.Add(0)
        try {
            for ($i = 1; $i -le $count; $i++) {
                $slide = $null; $shape = $null; $box = $null
                try {
                    $slide = $deck.Slides.Add($i, 12)
                    $shape = $slide.Shapes.AddTextbox(1, 40, 40, 600, 80)
                    $shape.TextFrame.TextRange.Text = "PowerPoint slide $i"
                    $shape.TextFrame.TextRange.Font.Size = 26
                    $shape.TextFrame.TextRange.Font.Color.RGB = 0xCC6622
                    $shape.ActionSettings.Item(1).Action = 7
                    $shape.ActionSettings.Item(1).Hyperlink.Address = "https://example.org/newsletter/slide-$i"
                    $box = $slide.Shapes.AddShape(1, 40, 150, 360, 120)
                    $box.Fill.ForeColor.RGB = 0x397D22
                    $box.TextFrame.TextRange.Text = "Green notice on slide $i"
                    $box.ActionSettings.Item(1).Action = 7
                    $box.ActionSettings.Item(1).Hyperlink.Address = 'appendix.pdf'
                    if ($i -eq 2) { $slide.SlideShowTransition.Hidden = -1 }
                } finally { Release-Com $box; Release-Com $shape; Release-Com $slide }
            }
            # A saved selection must not limit the app's full-presentation export.
            $printOptions = $null; $ranges = $null; $range = $null
            try {
                $printOptions = $deck.PrintOptions
                $ranges = $printOptions.Ranges
                $range = $ranges.Add($count, $count)
                $printOptions.RangeType = 4
            } finally { Release-Com $range; Release-Com $ranges; Release-Com $printOptions }
            $name = if ($count -eq 1) { 'Single slide.pptx' } else { 'Multiple slides.pptx' }
            $source = Join-Path $inputs $name
            $deck.SaveAs($source, 24)
            $cases += [pscustomobject]@{ Name=$name; Source=$source; ExpectedPages=$(if ($count -eq 1) {1} else {2}) }
            if ($count -eq 3) {
                $legacy = Join-Path $inputs 'Legacy slides.ppt'
                $deck.SaveCopyAs($legacy, 1)
                $cases += [pscustomobject]@{ Name='Legacy slides.ppt'; Source=$legacy; ExpectedPages=2 }
            }
        } finally {
            if ($null -ne $deck) { $deck.Saved = -1; $deck.Close(); Release-Com $deck; $deck = $null }
        }
    }
} finally {
    if ($null -ne $ppt) {
        if ($null -ne $oldAlerts) { $ppt.DisplayAlerts = $oldAlerts }
        if ($ppt.Presentations.Count -eq 0) { $ppt.Quit() }
        Release-Com $ppt
    }
}

$checks = @()
foreach ($case in $cases) {
    $before = (Get-FileHash $case.Source -Algorithm SHA256).Hash
    $snapshot = Join-Path $exports $case.Name
    Copy-Item $case.Source $snapshot
    $pdf = Join-Path $exports ([IO.Path]::GetFileNameWithoutExtension($case.Name) + '.pdf')
    $arguments = @('--office-export', ('"' + $snapshot + '"'), ('"' + $pdf + '"'), ('"' + $case.Source + '"'))
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit(180000)) { $process.Kill(); throw "PowerPoint export timed out: $($case.Name)" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path $pdf)) {
        $detail = if (Test-Path ($pdf + '.error.txt')) { [IO.File]::ReadAllText($pdf + '.error.txt') } else { 'No PDF produced.' }
        throw "PowerPoint export failed for $($case.Name): $detail"
    }
    $bytes = [IO.File]::ReadAllBytes($pdf)
    if ($bytes.Length -lt 100 -or [Text.Encoding]::ASCII.GetString($bytes, 0, 5) -ne '%PDF-') { throw "Invalid PDF: $pdf" }
    if ($before -ne (Get-FileHash $case.Source -Algorithm SHA256).Hash) { throw "Source changed: $($case.Name)" }
    # Office may update legacy working copies; the original must remain untouched.
    $snapshotUnchanged = $before -eq (Get-FileHash $snapshot -Algorithm SHA256).Hash
    $checks += [pscustomobject]@{ File=$case.Name; Exported=$true; SourceUnchanged=$true; SnapshotUnchanged=$snapshotUnchanged; ExpectedPages=$case.ExpectedPages; PdfBytes=$bytes.Length }
}
$result = [ordered]@{ Version=(Get-Item $Executable).VersionInfo.ProductVersion; PassedExports=$checks.Count; Checks=$checks }
$result | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $WorkDirectory 'powerpoint-results.json') -Encoding UTF8
$result | ConvertTo-Json -Depth 4
Write-Host "Generated fixtures and PDFs: $WorkDirectory"
Write-Host 'Also inspect the PDFs: single slide = 1 page; multiple/legacy = slides 1 and 3, with blue text, green boxes, web links and appendix links pointing to the inputs folder.'
