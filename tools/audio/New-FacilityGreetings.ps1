param([string]$Model = 'gemini-3.1-flash-tts-preview', [ValidateSet('WELCOME','NEW_NEED')][string[]]$Regenerate = @())
$ErrorActionPreference = 'Stop'
if ($Model -notmatch '^[a-zA-Z0-9._-]{1,128}$') { throw 'Invalid model name' }
if ([string]::IsNullOrWhiteSpace($env:PROPFLOW_GEMINI_API_KEY)) { throw 'Set PROPFLOW_GEMINI_API_KEY in the process environment; never pass the key on the command line.' }
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = [IO.Path]::GetFullPath((Join-Path $workspacePath 'src/PropFlow.Web.Client/wwwroot/audio/facility'))
if (-not $outputPath.StartsWith($workspacePath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output is outside the workspace' }
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$recordings = @(
    @{ kind='WELCOME'; file='welcome-vi.wav'; text='Chào bạn, mình là trợ lý tiện ích PropFlow. Hôm nay bạn muốn làm gì? Mình sẽ cùng bạn tìm hoạt động và thời gian phù hợp nhé.' },
    @{ kind='NEW_NEED'; file='new-need-vi.wav'; text='Mình nghe đây. Lần này bạn muốn tìm hoạt động gì?' }
)
$entries = @()
foreach ($item in $recordings) {
    $target = Join-Path $outputPath $item.file
    if ($item.kind -in $Regenerate -or -not (Test-Path -LiteralPath $target)) {
        $body = @{ contents=@(@{parts=@(@{text=('Đọc nguyên văn bằng tiếng Việt chuẩn, rõ ràng, tự nhiên, thân thiện; không đọc chỉ dẫn và không thêm nội dung: ' + $item.text)})}); generationConfig=@{responseModalities=@('AUDIO');speechConfig=@{voiceConfig=@{prebuiltVoiceConfig=@{voiceName='Kore'}}}} } | ConvertTo-Json -Depth 10
        $timer = [Diagnostics.Stopwatch]::StartNew()
        try {
            $response = Invoke-RestMethod -Method Post -Uri ('https://generativelanguage.googleapis.com/v1beta/models/' + $Model + ':generateContent') -Headers @{'x-goog-api-key'=$env:PROPFLOW_GEMINI_API_KEY} -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 60
        } catch { throw ('Speech generation failed; HTTP status ' + [int]$_.Exception.Response.StatusCode) }
        $timer.Stop()
        if ($response.candidates.Count -ne 1 -or $response.candidates[0].finishReason -ne 'STOP') { throw 'Incomplete speech response' }
        $parts = @($response.candidates[0].content.parts | Where-Object {$_.inlineData})
        if ($parts.Count -ne 1 -or $parts[0].inlineData.mimeType.Replace(' ','').ToLowerInvariant() -notin @('audio/l16;rate=24000','audio/l16;rate=24000;channels=1')) { throw 'Unexpected audio format' }
        [byte[]]$pcm = [Convert]::FromBase64String($parts[0].inlineData.data)
        if ($pcm.Length -lt 2 -or $pcm.Length % 2 -ne 0) { throw 'Invalid PCM data' }
        $stream = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($stream, [Text.Encoding]::ASCII, $true)
        try {
            $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int]($pcm.Length+36)); $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
            $writer.Write([int]16); $writer.Write([short]1); $writer.Write([short]1); $writer.Write([int]24000); $writer.Write([int]48000); $writer.Write([short]2); $writer.Write([short]16)
            $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int]$pcm.Length); $writer.Write($pcm); $writer.Flush()
            [IO.File]::WriteAllBytes($target,$stream.ToArray())
        } finally { $writer.Dispose(); $stream.Dispose() }
        [pscustomobject]@{Clip=$item.kind;GenerationMs=$timer.ElapsedMilliseconds;Bytes=([IO.FileInfo]$target).Length} | ConvertTo-Json -Compress | Write-Output
    }
    $entries += @{kind=$item.kind;file=$item.file;text=$item.text;sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash;bytes=([IO.FileInfo]$target).Length}
}
@{model=$Model;voice='Kore';language='vi-VN';sampleRate=24000;recordings=$entries} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'greetings.json') -Encoding utf8
