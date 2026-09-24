#Requires -Version 7.0
<#
.SYNOPSIS
    包内容安检：确保发布出去的 NuGet 包里没有配置文件、证书、私钥等敏感内容。

.DESCRIPTION
    包一旦发布就无法撤回（只能 unlist），所以把"安检"放在推送之前。
    release.yml 与 ci.yml 都会调用它；本地也可以随时手动跑：

        pwsh ./build/check-packages.ps1

.PARAMETER FeedPath
    待检查的包目录，默认 <仓库根>/local-feed。
#>
[CmdletBinding()]
param(
    [string]$FeedPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $FeedPath) {
    $FeedPath = Join-Path $repoRoot 'local-feed'
}

if (-not (Test-Path $FeedPath)) {
    throw "未找到包目录：$FeedPath"
}

# 确保 zip 相关类型可用：Windows PowerShell 需显式加载，PowerShell 7 通常已内置
try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }

# 禁止出现在包里的文件名模式
$forbiddenFilePatterns = @(
    'appsettings*.json',
    '.env',
    '.env.*',
    'secrets.json',
    '*.pfx',
    '*.p12',
    '*.snk',
    '*.pem',
    '*.key',
    '*.jks',
    '*.ppk',
    'id_rsa*',
    '*.pubxml',
    '*.rdp'
)

# 禁止出现在文本条目内容里的片段
$forbiddenContentPatterns = @(
    'BEGIN PRIVATE KEY',
    'BEGIN RSA PRIVATE KEY',
    'BEGIN CERTIFICATE',
    'AccountKey=',
    'SharedAccessKey=',
    'Password=',
    'SecretKey=',
    'ApiKey=',
    'client_secret'
)

$violations = [System.Collections.Generic.List[string]]::new()
$packages = @(Get-ChildItem -Path $FeedPath -Filter '*.nupkg' -File)

if ($packages.Count -eq 0) {
    throw "$FeedPath 中没有 .nupkg，请先执行打包。"
}

foreach ($package in $packages) {
    Write-Host "==> 安检 $($package.Name)" -ForegroundColor Cyan

    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)

    try {
        foreach ($entry in $zip.Entries) {
            $fileName = [System.IO.Path]::GetFileName($entry.FullName)

            foreach ($pattern in $forbiddenFilePatterns) {
                if ($fileName -like $pattern) {
                    $violations.Add("$($package.Name)：含禁止文件 $($entry.FullName)")
                }
            }

            $extension = [System.IO.Path]::GetExtension($entry.FullName).ToLowerInvariant()
            if ($extension -notin '.json', '.config', '.xml', '.nuspec', '.props', '.targets', '.md') {
                continue
            }

            $reader = New-Object System.IO.StreamReader($entry.Open())
            $text = $reader.ReadToEnd()
            $reader.Close()

            foreach ($fragment in $forbiddenContentPatterns) {
                if ($text.Contains($fragment, [StringComparison]::OrdinalIgnoreCase)) {
                    $violations.Add("$($package.Name)：$($entry.FullName) 含敏感片段 '$fragment'")
                }
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host '包内容安检未通过：' -ForegroundColor Red
    $violations | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw '安检失败，已阻止发布。'
}

Write-Host "包内容安检通过 ✅（共 $($packages.Count) 个包）" -ForegroundColor Green
