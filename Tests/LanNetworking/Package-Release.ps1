#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$ClientDirectory,
    [Parameter(Mandatory = $true)][string]$OutputZip,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$buildRoot = (Join-Path $projectRoot 'Build') + [IO.Path]::DirectorySeparatorChar
$clientRoot = (Resolve-Path -LiteralPath $ClientDirectory).Path
$zipPath = [IO.Path]::GetFullPath($OutputZip)
if (!$clientRoot.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase) -or
    !$zipPath.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw '客户端和发布 ZIP 必须位于本项目 Build 子目录内'
}
if (Test-Path -LiteralPath $zipPath) { throw '输出 ZIP 已存在；请选择新路径，避免覆盖之前的发布包' }
$headCommit = & git -c "safe.directory=$projectRoot" -C $projectRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $headCommit -ne $SourceCommit) { throw 'SourceCommit 必须是当前工程的 HEAD 提交' }
$changedRuntime = & git -c "safe.directory=$projectRoot" -C $projectRoot diff --name-only HEAD -- Assets/Script
if ($LASTEXITCODE -ne 0) { throw '无法检查运行时源码状态' }
if (@($changedRuntime | Where-Object { $_ -like '*.cs' -and $_ -notlike '*/Editor/*' }).Count -gt 0) {
    throw '运行时源码存在未提交修改，无法声明包对应 HEAD 提交'
}
$committedSources = @{}
$tree = & git -c "safe.directory=$projectRoot" -C $projectRoot ls-tree -r '--format=%(objectname) %(path)' $SourceCommit -- Assets/Script
if ($LASTEXITCODE -ne 0) { throw '无法读取源码提交的文件清单' }
foreach ($line in $tree) { $parts = $line.Split(' ', 2); $committedSources[$parts[1]] = $parts[0] }
$required = @('RMNetwork.exe', 'UnityPlayer.dll', 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',
    'RMNetwork_Data/globalgamemanagers', 'RMNetwork_Data/level0', 'RMNetwork_Data/level1',
    'RMNetwork_Data/level2', 'RMNetwork_Data/Managed/Assembly-CSharp.dll', 'RMNetwork_Data/Managed/Assembly-CSharp.pdb')
foreach ($relative in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $clientRoot $relative) -PathType Leaf)) { throw "缺少构建文件：$relative" }
}

# 不执行构建中的程序集：用 PDB 源码校验值确认此包来自当前源文件。
$pdbStream = [IO.File]::OpenRead((Join-Path $clientRoot 'RMNetwork_Data/Managed/Assembly-CSharp.pdb'))
$pdbProvider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
$runtimeSources = @()
try {
    $reader = $pdbProvider.GetMetadataReader()
    foreach ($handle in $reader.Documents) {
        $document = $reader.GetDocument($handle)
        $documentPath = $reader.GetString($document.Name).Replace('\', '/')
        $start = $documentPath.IndexOf('Assets/Script/', [StringComparison]::Ordinal)
        if ($start -lt 0) { continue }
        $relative = $documentPath.Substring($start)
        $sourcePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
        if (!$sourcePath.StartsWith((Join-Path $projectRoot 'Assets/Script') + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw "异常源码路径：$relative" }
        $expected = [Convert]::ToHexString($reader.GetBlobBytes($document.Hash))
        $actual = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        if ($expected -ne $actual) { throw "构建过期，源码已变化：$relative" }
        $sourceBytes = [Text.Encoding]::UTF8.GetBytes([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($sourcePath)).Replace("`r`n", "`n"))
        $header = [Text.Encoding]::UTF8.GetBytes(('blob ' + $sourceBytes.Length + [char]0))
        $gitBlob = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData([byte[]]($header + $sourceBytes))).ToLowerInvariant()
        if ($gitBlob -ne $committedSources[$relative]) { throw "构建源码与提交不同：$relative" }
        $runtimeSources += [ordered]@{ path = $relative; sha256 = $actual.ToLowerInvariant(); gitBlob = $gitBlob }
    }
} finally { $pdbProvider.Dispose() }
if ($runtimeSources.Count -eq 0) { throw 'PDB 中未找到可核对的运行时源码' }

$assemblyStream = [IO.File]::OpenRead((Join-Path $clientRoot 'RMNetwork_Data/Managed/Assembly-CSharp.dll'))
$pe = [Reflection.PortableExecutable.PEReader]::new($assemblyStream)
$protocol = $null
try {
    $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    foreach ($handle in $metadata.TypeDefinitions) {
        $type = $metadata.GetTypeDefinition($handle)
        if ($metadata.GetString($type.Name) -ne 'LanMessage') { continue }
        foreach ($fieldHandle in $type.GetFields()) {
            $field = $metadata.GetFieldDefinition($fieldHandle)
            if ($metadata.GetString($field.Name) -eq 'Protocol') {
                $constant = $metadata.GetConstant($field.GetDefaultValue())
                $protocol = [BitConverter]::ToInt32($metadata.GetBlobBytes($constant.Value), 0)
            }
        }
    }
    foreach ($handle in $metadata.AssemblyReferences) {
        $reference = $metadata.GetAssemblyReference($handle)
        $name = $metadata.GetString($reference.Name)
        if (!(Test-Path -LiteralPath (Join-Path $clientRoot ('RMNetwork_Data/Managed/' + $name + '.dll')))) {
            throw "缺少运行依赖：$name.dll"
        }
    }
} finally { $pe.Dispose() }
if ($null -eq $protocol) { throw '无法从客户端读取联机协议版本' }

$versionLine = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') |
    Where-Object { $_ -match '^m_EditorVersion: ' } | Select-Object -First 1
$manifest = [ordered]@{
    sourceCommit = $SourceCommit.ToLowerInvariant()
    sourceUrl = 'https://github.com/CityWithoutMe/rmuc-2026-simulator/tree/' + $SourceCommit
    unityVersion = $versionLine.Substring('m_EditorVersion: '.Length)
    protocol = $protocol
    buildKind = 'Development'
    packagedAtUtc = [DateTime]::UtcNow.ToString('o')
    runtimeSources = $runtimeSources
}
$files = @(Get-ChildItem -LiteralPath $clientRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '_DoNotShip([/\\]|$)' -and $_.Extension -notin @('.pdb', '.mdb')
} | Sort-Object FullName)
$checksumLines = @()
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($zipPath)) | Out-Null
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
function Add-TextEntry([string]$name, [string]$content) {
    $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try { $bytes = [Text.Encoding]::UTF8.GetBytes($content); $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}
try {
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($clientRoot, $file.FullName).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $checksumLines += (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative
    }
    Add-TextEntry 'README.txt' ([IO.File]::ReadAllText((Join-Path $projectRoot 'README/Windows客户端说明.txt')))
    Add-TextEntry 'BUILD-INFO.json' ($manifest | ConvertTo-Json -Depth 6)
    Add-TextEntry 'SHA256SUMS.txt' ($checksumLines -join "`n")
} finally { $zip.Dispose() }
[PSCustomObject]@{
    zip = $zipPath; sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceCommit = $SourceCommit; protocol = $protocol; verifiedRuntimeSources = $runtimeSources.Count
    runtimeFiles = $files.Count; bytes = (Get-Item -LiteralPath $zipPath).Length
} | ConvertTo-Json
