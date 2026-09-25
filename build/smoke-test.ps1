#Requires -Version 7.0
<#
.SYNOPSIS
    模板冒烟测试：打包 → 安装模板 → 生成项目 → 编译 → 跑测试 → 生成迁移并校验模型一致性。

.DESCRIPTION
    这是框架的质量闸门。它能拦住「框架包升级后业务项目编译不过」「设计时工厂不可用」
    「迁移与模型不同步」这类只有真实消费方才会暴露的问题 —— 单靠框架自身的单元测试发现不了。

.PARAMETER SkipPack
    跳过打包步骤，直接使用 local-feed 中已有的包（本地快速迭代时用）。

.PARAMETER OutputDir
    生成项目的输出目录，默认 <仓库根>/artifacts/smoke。

.EXAMPLE
    pwsh ./build/smoke-test.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipPack,

    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$templatePath = Join-Path $repoRoot 'packaging/CloudL.Templates/template'
$projectName = 'SmokeTest'

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot 'artifacts/smoke'
}

function Write-Step([string]$message) {
    Write-Host ""
    Write-Host "====> $message" -ForegroundColor Cyan
}

function Assert-LastExitCode([string]$what) {
    if ($LASTEXITCODE -ne 0) {
        throw "$what 失败（退出码 $LASTEXITCODE）"
    }
}

# ---------- 1. 打包框架包 ----------
if (-not $SkipPack) {
    Write-Step '打包框架包到 local-feed'
    & (Join-Path $PSScriptRoot 'pack.ps1') -SkipTemplateInstall
    Assert-LastExitCode '打包'
}

# ---------- 2. 安装模板（从刚打出的包安装，顺带验证打包结果本身可用） ----------
Write-Step '从包安装 dotnet new 模板'

$templatePackage = Get-ChildItem (Join-Path $repoRoot 'local-feed') -Filter 'CloudL.Templates.*.nupkg' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $templatePackage) {
    throw 'local-feed 中没有 CloudL.Templates 包，请先执行打包。'
}

$templateVersion = $templatePackage.Name.Substring('CloudL.Templates.'.Length).Replace('.nupkg', '')
Write-Host "使用模板包版本: $templateVersion" -ForegroundColor Yellow

& dotnet new uninstall $templatePath 2>&1 | Out-Null
& dotnet new uninstall CloudL.Templates 2>&1 | Out-Null
& dotnet new install "CloudL.Templates::$templateVersion"
Assert-LastExitCode '从包安装模板'

# ---------- 3. 生成业务项目 ----------
Write-Step "生成业务项目 $projectName"
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}

# 关键：显式指定刚打出来的框架版本。
# 否则模板默认的浮动版本会解析到已发布的旧包，冒烟测试就覆盖不到本次改动的代码。
$corePackage = Get-ChildItem (Join-Path $repoRoot 'local-feed') -Filter 'CloudL.Core.*.nupkg' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $corePackage) {
    throw '"local-feed 中没有 CloudL.Core 包，请先执行打包。'
}

$frameworkVersion = $corePackage.Name.Substring('CloudL.Core.'.Length).Replace('.nupkg', '')
Write-Host "使用框架版本: $frameworkVersion" -ForegroundColor Yellow

& dotnet new cloudl -n $projectName -o $OutputDir --frameworkVersion $frameworkVersion
Assert-LastExitCode '生成项目'

$solution = Join-Path $OutputDir "$projectName.slnx"
if (-not (Test-Path $solution)) {
    throw "未找到生成的解决方案文件：$solution"
}

# ---------- 4. 编译 ----------
Write-Step '编译生成的项目'
& dotnet build $solution -c Debug --nologo
Assert-LastExitCode '编译生成的项目'

# ---------- 5. 运行生成的测试 ----------
Write-Step '运行生成项目的单元测试'
& dotnet test (Join-Path $OutputDir "tests/$projectName.Tests/$projectName.Tests.csproj") --nologo -v q
Assert-LastExitCode '运行测试'

# ---------- 6. EF 迁移：能生成 + 与模型一致 ----------
# 说明：这里刻意不传 --startup-project，走 EF 项目自身的 IDesignTimeDbContextFactory。
# 该工厂会从 Web 项目读取连接串与提供程序，且不依赖当前工作目录。
$efProject = Join-Path $OutputDir "src/$projectName.EntityFrameworkCore/$projectName.EntityFrameworkCore.csproj"

& dotnet ef --version 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warning "未安装 dotnet-ef 工具，已跳过迁移检查。安装：dotnet tool install --global dotnet-ef"
}
else {
    # 6.1 验证设计时工厂可用（能解析连接串与提供程序）并成功生成迁移
    Write-Step '生成初始迁移（校验设计时工厂）'
    & dotnet ef migrations add InitialSmoke --project $efProject --output-dir Migrations --no-build
    Assert-LastExitCode '生成迁移'

    # 新迁移需要进入程序集后才能被 has-pending-model-changes 看到
    & dotnet build $efProject --nologo | Out-Null
    Assert-LastExitCode '重新编译 EF 项目'

    # 6.2 确认模型与迁移一致：拦住「改了实体却没加迁移」
    Write-Step '校验模型与迁移一致'
    & dotnet ef migrations has-pending-model-changes --project $efProject --no-build
    if ($LASTEXITCODE -ne 0) {
        throw '模型存在未包含在迁移中的变更：请执行 dotnet ef migrations add <Name>'
    }

    Write-Host '迁移与模型一致' -ForegroundColor Green
}

Write-Host ""
Write-Host '冒烟测试通过 ✅' -ForegroundColor Green
