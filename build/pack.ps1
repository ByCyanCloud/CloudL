#Requires -Version 7.0
<#
.SYNOPSIS
    打包 CloudL 框架 NuGet 包，推送到本机源，并按需重装 dotnet new 模板。

.DESCRIPTION
    1. 依次打包 src/ 下的三个框架项目到 local-feed（或 -FeedPath 指定的目录）；
    2. 可选：推送到远程源（GitHub Packages / nuget.org）；
    3. 可选：从刚打出的 CloudL.Templates 包安装 dotnet new 模板，使 `dotnet new cloudl` 可用。

.PARAMETER Configuration
    构建配置，默认 Release。

.PARAMETER Version
    显式指定包版本（覆盖 MinVer 的 git tag 计算），例如 0.1.0 或 1.2.3-preview.1。

.PARAMETER FeedPath
    本地源目录，默认 <仓库根>/local-feed。

.PARAMETER PushSource
    远程源名称或 URL（例如 github 或 https://api.nuget.org/v3/index.json）。提供后才会推送。

.PARAMETER ApiKey
    推送所需的 API Key。

.PARAMETER SkipTemplateInstall
    跳过模板重装。

.EXAMPLE
    pwsh ./build/pack.ps1
    打包到 local-feed 并重装模板。

.EXAMPLE
    pwsh ./build/pack.ps1 -Version 0.2.0 -PushSource github -ApiKey $env:GITHUB_TOKEN
    以 0.2.0 打包并推送到 GitHub Packages。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version,

    [string]$FeedPath,

    [string]$PushSource,

    [string]$ApiKey,

    [switch]$SkipTemplateInstall
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $FeedPath) {
    $FeedPath = Join-Path $repoRoot 'local-feed'
}

if (-not (Test-Path $FeedPath)) {
    New-Item -ItemType Directory -Force -Path $FeedPath | Out-Null
}

$FeedPath = (Resolve-Path $FeedPath).Path

# ---------- 清理旧的预览包 ----------
# local-feed 会随每次打包不断累积 0.1.1-preview.0.x，这里只保留稳定版与最新的一个预览版。
$feedPackages = @(
    Get-ChildItem -Path $FeedPath -Filter 'CloudL.*.nupkg' -File -ErrorAction SilentlyContinue
    Get-ChildItem -Path $FeedPath -Filter 'CloudL.*.snupkg' -File -ErrorAction SilentlyContinue
)

$versionedPackages = foreach ($package in $feedPackages) {
    if ($package.Name -match '^CloudL\..+?\.(.+)\.(nupkg|snupkg)$') {
        [pscustomobject]@{ Path = $package.FullName; Version = $Matches[1] }
    }
}

$previewPackages = $versionedPackages | Where-Object { $_.Version -like '*preview*' }

if ($previewPackages) {
    $latestPreviewVersion = $previewPackages |
        Group-Object Version |
        Sort-Object { ($_.Group | Measure-Object -Property LastWriteTime -Maximum).Maximum } -Descending |
        Select-Object -First 1 -ExpandProperty Name

    $stalePackages = @($previewPackages | Where-Object { $_.Version -ne $latestPreviewVersion })

    foreach ($stale in $stalePackages) {
        Remove-Item -LiteralPath $stale.Path -Force -ErrorAction SilentlyContinue
    }

    if ($stalePackages.Count -gt 0) {
        Write-Host "==> 已清理 $($stalePackages.Count) 个旧预览包（保留 $latestPreviewVersion）" -ForegroundColor Yellow
    }
}

$projects = @(
    'src/CloudL.Core/CloudL.Core.csproj',
    'src/CloudL.EntityFrameworkCore/CloudL.EntityFrameworkCore.csproj',
    'src/CloudL.EntityFrameworkCore.PostgreSql/CloudL.EntityFrameworkCore.PostgreSql.csproj',
    'src/CloudL.EntityFrameworkCore.SqlServer/CloudL.EntityFrameworkCore.SqlServer.csproj',
    'src/CloudL.AspNetCore/CloudL.AspNetCore.csproj',
    'packaging/CloudL.Templates/CloudL.Templates.csproj'
)

Write-Host "==> 输出目录: $FeedPath" -ForegroundColor Cyan
if ($Version) {
    Write-Host "==> 指定版本: $Version" -ForegroundColor Cyan
}

$produced = @()

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "==> 打包 $project" -ForegroundColor Cyan

    $packArgs = @('pack', $projectPath, '-c', $Configuration, '-o', $FeedPath, '--nologo')
    if ($Version) {
        $packArgs += "/p:MinVerVersionOverride=$Version"
    }

    & dotnet @packArgs
    if ($LASTEXITCODE -ne 0) {
        throw "打包失败: $project"
    }
}

# 每个包 ID 取最新一个：包数会随框架增长，写死数量迟早会漏掉
$produced = Get-ChildItem -Path $FeedPath -Filter 'CloudL.*.nupkg' -File |
    Group-Object { if ($_.Name -match '^(CloudL\..+?)\.\d+\.\d+') { $Matches[1] } else { $_.Name } } |
    ForEach-Object { $_.Group | Sort-Object LastWriteTime -Descending | Select-Object -First 1 } |
    Sort-Object Name

if ($produced.Count -eq 0) {
    throw "未在 $FeedPath 找到任何 CloudL.*.nupkg，请检查打包输出。"
}

Write-Host "==> 已生成:" -ForegroundColor Green
$produced | ForEach-Object { Write-Host "    $($_.Name)" }

if ($PushSource) {
    foreach ($package in $produced) {
        Write-Host "==> 推送 $($package.Name) -> $PushSource" -ForegroundColor Cyan

        $pushArgs = @('nuget', 'push', $package.FullName, '--source', $PushSource, '--skip-duplicate')
        if ($ApiKey) {
            $pushArgs += @('--api-key', $ApiKey)
        }

        & dotnet @pushArgs
        if ($LASTEXITCODE -ne 0) {
            throw "推送失败: $($package.Name)"
        }
    }
}

if (-not $SkipTemplateInstall) {
    Write-Host "==> 重装 dotnet new 模板（从本次打出的包安装，与使用者的安装路径完全一致）" -ForegroundColor Cyan

    $templatePackage = Get-ChildItem -Path $FeedPath -Filter 'CloudL.Templates.*.nupkg' -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $templatePackage) {
        throw "未在 $FeedPath 找到 CloudL.Templates 包，无法安装模板。"
    }

    $templateVersion = $templatePackage.Name.Substring('CloudL.Templates.'.Length).Replace('.nupkg', '')

    # 源码目录安装与包安装会互相冲突，两种都先卸掉（未安装时返回非零，属正常情况）
    & dotnet new uninstall (Join-Path $repoRoot 'packaging/CloudL.Templates/template') 2>&1 | Out-Null
    & dotnet new uninstall CloudL.Templates 2>&1 | Out-Null

    & dotnet new install "CloudL.Templates::$templateVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "模板安装失败: CloudL.Templates::$templateVersion"
    }

    Write-Host "==> 模板已就绪（$templateVersion）：dotnet new cloudl -n MyApp" -ForegroundColor Green
}

Write-Host "==> 完成" -ForegroundColor Green
