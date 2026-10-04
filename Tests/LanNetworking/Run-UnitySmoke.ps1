param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [ValidateRange(1024, 65535)][int]$Port = 17777,
    [ValidateRange(1, 4)][int]$Players = 4,
    [ValidateRange(0, 25)][int]$StallClientSeconds = 0,
    [ValidateRange(0, 25)][int]$StallHostSeconds = 0,
    [ValidateRange(11, 120)][int]$DurationSeconds = 11,
    [switch]$Graphics,
    [switch]$Feedback
)
$ErrorActionPreference = 'Stop'
$buildExe = (Resolve-Path -LiteralPath $Executable).Path
if ([IO.Path]::GetFileName($buildExe) -ne 'RMNetwork.exe') { throw '请选择开发构建 RMNetwork.exe' }
$reportRoot = Join-Path (Split-Path (Split-Path $buildExe -Parent) -Parent) 'LanSmoke'
$runDirectory = Join-Path $reportRoot (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
New-Item -ItemType Directory -Path $runDirectory | Out-Null
function PortListening {
    return @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object { $_.Port -eq $Port }).Count -gt 0
}
if (PortListening) { throw "测试端口 $Port 已占用，请换一个端口" }
$testProcesses = @()
try {
    for ($slot = 0; $slot -lt $Players; $slot++) {
        $role = if ($slot -eq 0) { 'host' } else { 'client' }
        $report = Join-Path $runDirectory "$slot.json"
        $log = Join-Path $runDirectory "$slot.log"
        $testArgs = @('-lanTestRole', $role, '-lanTestSlot', $slot, '-lanTestPlayers', $Players,
            '-lanTestPort', $Port, '-lanTestReport', ('"' + $report + '"'), '-logFile', ('"' + $log + '"'))
        $testDuration = [Math]::Max($DurationSeconds, (11 + [Math]::Max($StallClientSeconds, $StallHostSeconds)))
        $testArgs += @('-lanTestDuration', $testDuration)
        if ($Feedback) { $testArgs += '-lanTestFeedback' }
        if (!$Graphics) { $testArgs += @('-batchmode', '-nographics') }
        else { $testArgs += @('-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360') }
        if ($slot -eq $Players - 1 -and $slot -gt 0) { $testArgs += @('-lanTestStallSeconds', $StallClientSeconds) }
        if ($slot -eq 0) { $testArgs += @('-lanTestStallSeconds', $StallHostSeconds) }
        $testProcesses += Start-Process -FilePath $buildExe -WindowStyle Hidden -PassThru -ArgumentList $testArgs
        if ($slot -eq 0) {
            $deadline = [DateTime]::UtcNow.AddSeconds(45)
            while (!(PortListening)) {
                if ($testProcesses[0].HasExited -or [DateTime]::UtcNow -gt $deadline) { throw "主机未开始监听，查看 $log" }
                Start-Sleep -Milliseconds 200
            }
        }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($testDuration + 90)
    while (@($testProcesses | Where-Object { !$_.HasExited }).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "测试进程超时，查看 $runDirectory" }
        Start-Sleep -Milliseconds 500
    }
    $failed = $false
    for ($slot = 0; $slot -lt $Players; $slot++) {
        $report = Join-Path $runDirectory "$slot.json"
        if (!(Test-Path -LiteralPath $report)) { throw "玩家 $slot 缺少报告，查看 $runDirectory" }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        $result | ConvertTo-Json -Compress | Write-Output
        if (!$result.success) { $failed = $true }
    }
    if ($failed) { throw "${Players}人联机测试失败，查看 $runDirectory" }
    Write-Output "PASS: ${Players}人 Unity 联机测试；报告：$runDirectory"
}
finally {
    # 只结束本脚本直接启动且尚未退出的进程，不查找或关闭用户的 Unity 编辑器。
    foreach ($player in $testProcesses) {
        if (!$player.HasExited) { $player.Kill() }
        $player.Dispose()
    }
}
