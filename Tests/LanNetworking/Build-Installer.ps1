#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$PackageZip,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$CompilerPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$buildRoot = (Join-Path $projectRoot 'Build') + [IO.Path]::DirectorySeparatorChar
$zipPath = (Resolve-Path -LiteralPath $PackageZip).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (!$zipPath.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase) -or
    !$outputRoot.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '包和输出目录必须位于本项目 Build 内' }
if (!$CompilerPath) { $CompilerPath = Join-Path $buildRoot 'Tools/InnoSetup/ISCC.exe' }
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$payload = Join-Path $outputRoot 'installer-payload'
$installer = Join-Path $outputRoot "RMUC2026-Simulator-$Version-Windows-Setup.exe"
if ((Test-Path -LiteralPath $payload) -or (Test-Path -LiteralPath $installer)) { throw '安装包或暂存目录已存在，避免覆盖已有发布' }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $zip.Entries) {
        $destination = [IO.Path]::GetFullPath((Join-Path $payload $entry.FullName))
        if (!$destination.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "异常 ZIP 路径：$($entry.FullName)"
        }
    }
} finally { $zip.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $payload)
$manifest = Get-Content -LiteralPath (Join-Path $payload 'BUILD-INFO.json') -Raw | ConvertFrom-Json
$head = & git -c "safe.directory=$projectRoot" -C $projectRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $manifest.sourceCommit -ne $head) { throw '安装包源码提交与当前 HEAD 不一致' }
foreach ($line in Get-Content -LiteralPath (Join-Path $payload 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw '无效校验清单' }
    $expectedHash = $Matches[1]
    $file = [IO.Path]::GetFullPath((Join-Path $payload $Matches[2]))
    if (!$file.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
        throw "安装包内容校验失败：$file"
    }
}
& $compiler "/DClientDirectory=$payload" "/DAppVersion=$Version" "/DOutputDirectory=$outputRoot" (Join-Path $PSScriptRoot 'Windows-Installer.iss')
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $installer)) { throw 'Inno Setup 编译失败' }
[ordered]@{
    installer = $installer
    sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceCommit = $manifest.sourceCommit
    protocol = $manifest.protocol
    version = $Version
} | ConvertTo-Json
