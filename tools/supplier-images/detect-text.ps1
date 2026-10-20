<#
    MOTOR: finds text (brand names, model codes, labels) in pictures with the OCR engine built into Windows.
    Used after AI upscaling: an upscaler invents detail and garbles small text, so every picture with text is
    listed for a human check. Writes one JSON object per line: {"file": "...", "text": "...", "words": n}.

    Usage: powershell -NoProfile -ExecutionPolicy Bypass -File detect-text.ps1 -ImageDir <dir> -Out <ocr.jsonl>
#>
param(
    [Parameter(Mandatory = $true)][string]$ImageDir,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime]

$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    })[0]

function Await($operation, [Type]$resultType) {
    $task = $asTaskGeneric.MakeGenericMethod($resultType).Invoke($null, @($operation))
    $task.Wait(-1) | Out-Null
    $task.Result
}

$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if ($null -eq $engine) { throw 'No Windows OCR language is installed.' }

$lines = New-Object System.Collections.Generic.List[string]
foreach ($image in Get-ChildItem -LiteralPath $ImageDir -File | Where-Object { $_.Extension -match '^\.(jpg|jpeg|png)$' }) {
    $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($image.FullName)) ([Windows.Storage.StorageFile])
    $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
    try {
        $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $words = @($result.Lines | ForEach-Object { $_.Words } | Where-Object { $_.Text -match '[\p{L}\p{N}]{2,}' })
        $text = ($words | ForEach-Object { $_.Text }) -join ' '
        $lines.Add((@{ file = $image.Name; text = $text; words = $words.Count } | ConvertTo-Json -Compress))
    }
    finally {
        $stream.Dispose()
    }
}

[System.IO.File]::WriteAllLines($Out, $lines, (New-Object System.Text.UTF8Encoding $false))
Write-Output "ocr: $($lines.Count) pictures -> $Out"
