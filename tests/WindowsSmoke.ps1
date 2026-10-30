param(
    [string]$Executable = (Join-Path $PSScriptRoot '../dist/GoodSort PDF.exe'),
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('Newsletter-Smoke-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path $Executable).Path
$null = New-Item -ItemType Directory -Path $WorkDirectory -Force
$WorkDirectory = (Resolve-Path $WorkDirectory).Path
$newsletters = Join-Path $WorkDirectory 'Newsletters'
$notices = Join-Path $WorkDirectory 'Notices'
$exports = Join-Path $WorkDirectory 'Exports'
$null = New-Item -ItemType Directory -Path $newsletters, $notices, $exports -Force

$word = $null; $doc = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3
    $doc = $word.Documents.Add()
    $doc.Content.Text = "Year 3 newsletter`rSchool website`rEmail the school`rColour sample: blue heading, green body.`r"
    $doc.Content.Font.Size = 16
    $doc.Content.Font.Color = 0x397D22
    $doc.Paragraphs.Item(1).Range.Font.Color = 0xCC6622
    $doc.Paragraphs.Item(1).Range.Font.Size = 28
    $null = $doc.Hyperlinks.Add($doc.Paragraphs.Item(2).Range, 'https://example.org/newsletter')
    $null = $doc.Hyperlinks.Add($doc.Paragraphs.Item(3).Range, 'mailto:school@example.org')
    $doc.SaveAs2((Join-Path $newsletters 'Year 3 newsletter.docx'), 16)
} finally {
    if ($null -ne $doc) { $doc.Close(0); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($doc) }
    if ($null -ne $word) { $word.Quit(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) }
}

$powerpoint = $null; $deck = $null
try {
    $powerpoint = New-Object -ComObject PowerPoint.Application
    $deck = $powerpoint.Presentations.Add(0)
    $slide = $deck.Slides.Add(1, 12)
    $shape = $slide.Shapes.AddTextbox(1, 40, 40, 600, 80)
    $shape.TextFrame.TextRange.Text = 'Year 4 newsletter — school website'
    $shape.TextFrame.TextRange.Font.Size = 26
    $shape.TextFrame.TextRange.Font.Color.RGB = 0xCC6622
    $shape.ActionSettings.Item(1).Hyperlink.Address = 'https://example.org/newsletter'
    $box = $slide.Shapes.AddShape(1, 40, 150, 360, 120)
    $box.Fill.ForeColor.RGB = 0x397D22
    $box.TextFrame.TextRange.Text = 'Green notice area'
    $deck.SaveAs((Join-Path $newsletters 'Year 4 newsletter.pptx'), 24)
} finally {
    if ($null -ne $deck) { $deck.Saved = -1; $deck.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($deck) }
    if ($null -ne $powerpoint) {
        if ($powerpoint.Presentations.Count -eq 0) { $powerpoint.Quit() }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($powerpoint)
    }
}

Add-Type -AssemblyName System.Drawing
$bitmap = New-Object Drawing.Bitmap(1000, 600)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$font = New-Object Drawing.Font('Segoe UI', 38)
try {
    $graphics.Clear([Drawing.Color]::FromArgb(23, 108, 90))
    $graphics.DrawString('Community notice', $font, [Drawing.Brushes]::White, 65, 220)
    $bitmap.Save((Join-Path $notices 'Community notice.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }

foreach ($source in Get-ChildItem $newsletters -File) {
    # Export copies, just as the GUI does, so the test cannot alter the fixtures.
    $snapshot = Join-Path $exports $source.Name
    Copy-Item $source.FullName $snapshot
    $pdf = Join-Path $exports ($source.BaseName + '.pdf')
    $arguments = @('--office-export', ('"' + $snapshot + '"'), ('"' + $pdf + '"'), ('"' + $source.FullName + '"'))
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit(180000)) {
        $process.Kill()
        throw "Office export timed out for $($source.Name). Check for Office prompts."
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path $pdf)) {
        $detail = if (Test-Path ($pdf + '.error.txt')) { Get-Content ($pdf + '.error.txt') -Raw } else { 'No PDF produced.' }
        throw "Export failed for $($source.Name): $detail"
    }
    $bytes = [IO.File]::ReadAllBytes($pdf)
    if ($bytes.Length -lt 100 -or [Text.Encoding]::ASCII.GetString($bytes, 0, 5) -ne '%PDF-') { throw "Invalid PDF: $pdf" }
    Write-Host "PASS Office export: $($source.Name)"
}
Write-Host "Fixtures: $WorkDirectory"
Write-Host 'Open the app. Drop Newsletters on the left and Notices on the right, select Term 2 / Weeks 5–6, then GO.'
Write-Host 'Check both year PDFs contain their newsletter followed by the green notice, with clickable web/email links and matching colours.'
Start-Process -FilePath $Executable
