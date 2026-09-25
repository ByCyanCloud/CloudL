#Requires -Version 7.0
<#
.SYNOPSIS
    包内容安检：确保发布出去的 NuGet 包里没有配置文件、证书、私钥等敏感内容。

.DESCRIPTION
    包一旦发布就无法撤回（只能 unlist），所以把"安检"放在推送之前。
    release.yml 与 ci.yml 都会调用它；本地也可以随时手动跑：

        pwsh ./build/check-packages.ps1

    规则设计原则：只匹配<strong>真凭据形态</strong>，不做纯关键字匹配。
    因为文档、安全代码里出现 client_secret / password 这类词是完全正常的，
    按关键字拦会造成误报，最终让人习惯性绕过安检 —— 那比没有安检更糟。

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

# 禁止出现在文本条目内容里的“真凭据”特征。
# 关键字后面（可隔一个引号，兼容 JSON 属性写法）必须跟一个足够长的不透明值，才算真凭据。
$forbiddenContentPatterns = @(
    'BEGIN PRIVATE KEY',
    'BEGIN RSA PRIVATE KEY',
    'BEGIN OPENSSH PRIVATE KEY',
    'BEGIN CERTIFICATE',
    '(?i)(password|passwd|pwd|secret|secretkey|apikey|accountkey|sharedaccesskey|client_secret|connectionstring)[\x22\x27]?\s*[=:]\s*[\x22\x27]?[A-Za-z0-9+/_\-\.]{12,}',
    '(?i)(AccountKey|SharedAccessKey|sig)=[A-Za-z0-9%+/_\-]{20,}'
)

# 模板包按设计就包含 appsettings*.json（占位配置，值为空或明显占位符），
# 因此只对它们豁免“文件名规则”；<strong>内容规则照旧生效</strong> ——
# 真凭据被粘进模板的 appsettings 里，一样会被拦下。
$templatePackageIds = @('CloudL.Templates')

function Test-IsTemplatePackage {
    param([Parameter(Mandatory)][string]$PackageName)

    foreach ($packageId in $templatePackageIds) {
        if ($PackageName -like "$packageId.*") {
            return $true
        }
    }

    return $false
}

$violations = [System.Collections.Generic.List[string]]::new()
$packages = @(Get-ChildItem -Path $FeedPath -Filter '*.nupkg' -File)

if ($packages.Count -eq 0) {
    throw "$FeedPath 中没有 .nupkg，请先执行打包。"
}

foreach ($package in $packages) {
    Write-Host "==> 安检 $($package.Name)" -ForegroundColor Cyan

    $isTemplatePackage = Test-IsTemplatePackage -PackageName $package.Name
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)

    try {
        foreach ($entry in $zip.Entries) {
            $fileName = [System.IO.Path]::GetFileName($entry.FullName)

            if (-not $isTemplatePackage) {
                foreach ($pattern in $forbiddenFilePatterns) {
                    if ($fileName -like $pattern) {
                        $violations.Add("$($package.Name)：含禁止文件 $($entry.FullName)")
                    }
                }
            }

            $extension = [System.IO.Path]::GetExtension($entry.FullName).ToLowerInvariant()
            if ($extension -notin '.json', '.config', '.xml', '.nuspec', '.props', '.targets', '.md', '.txt', '.yml', '.yaml') {
                continue
            }

            $reader = New-Object System.IO.StreamReader($entry.Open())
            $text = $reader.ReadToEnd()
            $reader.Close()

            foreach ($pattern in $forbiddenContentPatterns) {
                if ([regex]::IsMatch($text, $pattern)) {
                    $violations.Add("$($package.Name)：$($entry.FullName) 命中敏感特征 /$pattern/")
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
