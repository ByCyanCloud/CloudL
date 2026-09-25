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

# ---------- 7. 运行时管道检查：健康检查必须匿名可访问 ----------
# 来自一个真实缺陷：模板的 FallbackPolicy 要求认证，而 MapHealthChecks 没有 AllowAnonymous，
# 负载均衡 / k8s 探针会拿到 401 并判定"永远不健康" —— 这类管道装配问题编译期看不出来。
Write-Step '运行时检查：/health 可被探针匿名访问'

$webProject = Join-Path $OutputDir "src/$projectName.Web/$projectName.Web.csproj"
$webPort = 10003   # 与模板 appsettings.json 的 Kestrel 端口一致
# 注意：不要用 -WindowStyle（Linux 上不受支持，会让整步直接失败）；应用输出落盘以便失败时诊断
$appLog = Join-Path ([System.IO.Path]::GetTempPath()) 'cloudl-smoke-app.log'
$probe = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', $webProject, '--no-build') -PassThru -RedirectStandardOutput $appLog -RedirectStandardError "$appLog.err"

try {
    $healthStatus = 0

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $response = Invoke-WebRequest "http://localhost:$webPort/health" -TimeoutSec 30
            $healthStatus = [int]$response.StatusCode
            break
        }
        catch {
            if ($_.Exception.Response) {
                $healthStatus = [int]$_.Exception.Response.StatusCode
                break
            }

            Start-Sleep -Seconds 2   # 应用还在启动
        }
    }

    if ($healthStatus -eq 0) {
        $tail = (Get-Content -LiteralPath $appLog -Tail 20 -ErrorAction SilentlyContinue) -join ' | '
        Write-Host "::error::/health 无响应（应用可能未启动）。应用日志尾部：$tail"
        throw '应用未能在预期时间内响应 /health，无法完成运行时检查。'
    }

    if ($healthStatus -eq 401 -or $healthStatus -eq 403) {
        Write-Host "::error::/health 返回 $healthStatus，探针会被挡在认证之外"
        throw "/health 返回 $healthStatus：探针会被挡在认证之外。请确认 MapHealthChecks 上加了 AllowAnonymous。"
    }

    Write-Host "    /health -> HTTP $healthStatus（非 401/403，探针可用）" -ForegroundColor Green
}
finally {
    if ($IsWindows) { taskkill /PID $probe.Id /T /F 2>&1 | Out-Null }
    else { Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host '冒烟测试通过 ✅' -ForegroundColor Green
