#Requires -Version 7.0
<#
.SYNOPSIS
    打包 Livia 框架 NuGet 包，推送到本机源，并按需重装 dotnet new 模板。

.DESCRIPTION
    1. 依次打包 src/ 下的三个框架项目到 local-feed（或 -FeedPath 指定的目录）；
    2. 可选：推送到远程源（GitHub Packages / nuget.org）；
    3. 可选：重装 template/ 下的 dotnet new 模板，使 `dotnet new livia` 可用。

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

$projects = @(
    'src/Livia.Core/Livia.Core.csproj',
    'src/Livia.EntityFrameworkCore/Livia.EntityFrameworkCore.csproj',
    'src/Livia.AspNetCore/Livia.AspNetCore.csproj'
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

$produced = Get-ChildItem -Path $FeedPath -Filter 'Livia.*.nupkg' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 3

if ($produced.Count -eq 0) {
    throw "未在 $FeedPath 找到任何 Livia.*.nupkg，请检查打包输出。"
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
    $templatePath = Join-Path $repoRoot 'template'

    if (Test-Path $templatePath) {
        Write-Host "==> 重装 dotnet new 模板" -ForegroundColor Cyan

        # 先卸载旧版本（未安装时返回非零，属正常情况）
        & dotnet new uninstall $templatePath 2>&1 | Out-Null

        & dotnet new install $templatePath
        if ($LASTEXITCODE -ne 0) {
            throw "模板安装失败: $templatePath"
        }

        Write-Host "==> 模板已就绪：dotnet new livia -n MyApp" -ForegroundColor Green
    }
    else {
        Write-Warning "未找到模板目录，已跳过：$templatePath"
    }
}

Write-Host "==> 完成" -ForegroundColor Green
