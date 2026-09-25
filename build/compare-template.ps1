#Requires -Version 7.0
<#
.SYNOPSIS
    把一个已有的业务项目，与"用当前模板重新生成"的结果做比对，找出装配类文件的差异。

.DESCRIPTION
    为什么需要它：业务项目是用 dotnet new 生成的一份**复制**，不是对框架包的依赖。
    所以升级 CloudL.* 包能让框架代码更新，但模板生成的装配文件（Program.cs、*Module.cs、
    设计时工厂、Directory.Packages.props…）**不会**跟着更新 —— 这个脚本就是补这一步。

    做法：用本地 feed 里最新的 CloudL.Templates 包，按**同样的项目名**生成一个临时项目，
    再逐个比对装配类文件。项目名一致，所以差异都是"真差异"，不含命名噪音。

.PARAMETER ProjectPath
    已有业务项目的根目录（其中应含 <名称>.slnx）。

.PARAMETER FrameworkVersion
    指定生成用的框架版本；默认取 local-feed 里最新的 CloudL.Core 包版本。

.PARAMETER DiffFile
    差异输出文件路径，默认 <仓库>/artifacts/template-compare/diff.txt。

.EXAMPLE
    pwsh ./build/compare-template.ps1 -ProjectPath ..\MyCompany.BookStore
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [string]$FrameworkVersion,
    [string]$DiffFile
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$feedPath = Join-Path $repoRoot 'local-feed'

if (-not (Test-Path $ProjectPath)) {
    throw "未找到业务项目目录：$ProjectPath"
}

$ProjectPath = (Resolve-Path $ProjectPath).Path

# ---------- 1. 推导项目名（与生成时一致，差异才干净） ----------
$solution = Get-ChildItem -Path $ProjectPath -Filter '*.slnx' -File | Select-Object -First 1

if (-not $solution) {
    throw "未在 $ProjectPath 找到 .slnx，无法推导项目名。"
}

$projectName = $solution.BaseName
Write-Host "==> 业务项目: $projectName" -ForegroundColor Cyan

# ---------- 2. 解析要使用的框架版本 ----------
if (-not $FrameworkVersion) {
    $corePackage = Get-ChildItem -Path $feedPath -Filter 'CloudL.Core.*.nupkg' -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $corePackage) {
        throw "local-feed 中没有 CloudL.Core 包，请先在框架仓库执行 build/pack.ps1。"
    }

    $FrameworkVersion = $corePackage.Name.Substring('CloudL.Core.'.Length).Replace('.nupkg', '')
}

$templatePackage = Get-ChildItem -Path $feedPath -Filter 'CloudL.Templates.*.nupkg' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $templatePackage) {
    throw "local-feed 中没有 CloudL.Templates 包，请先在框架仓库执行 build/pack.ps1。"
}

$templateVersion = $templatePackage.Name.Substring('CloudL.Templates.'.Length).Replace('.nupkg', '')
Write-Host "==> 框架版本: $FrameworkVersion（模板包 $templateVersion）" -ForegroundColor Cyan

# ---------- 3. 从包装模板并生成临时项目 ----------
$compareRoot = Join-Path $repoRoot 'artifacts/template-compare'
$tempProject = Join-Path $compareRoot $projectName

if (Test-Path $tempProject) {
    Remove-Item -Path $tempProject -Recurse -Force
}

& dotnet new uninstall (Join-Path $repoRoot 'packaging/CloudL.Templates/template') 2>&1 | Out-Null
& dotnet new uninstall CloudL.Templates 2>&1 | Out-Null
& dotnet new install "CloudL.Templates::$templateVersion"
if ($LASTEXITCODE -ne 0) {
    throw "模板安装失败：CloudL.Templates::$templateVersion"
}

Write-Host "==> 生成临时项目用于比对" -ForegroundColor Cyan
& dotnet new cloudl -n $projectName -o $tempProject --frameworkVersion $FrameworkVersion
if ($LASTEXITCODE -ne 0) {
    throw '生成临时项目失败。'
}

# ---------- 4. 比对装配类文件 ----------
# 只看"框架装配"相关的文件：项目文件、装配模块、启动入口、设计时工厂、根级配置。
# 业务代码（实体/服务/控制器）不在比对范围内 —— 那些本来就该由你自己演进。
$patterns = @(
    'Directory.Build.props',
    'Directory.Packages.props',
    '.editorconfig',
    'src/*/*.csproj',
    'src/*/Program.cs',
    'src/*/*Module.cs',
    'src/*/Data/DesignTimeDbContextFactory.cs',
    'src/*/Mappings/*Register.cs'
)

function Get-WiringFiles {
    param([string]$Root)

    $found = [System.Collections.Generic.List[string]]::new()

    foreach ($pattern in $patterns) {
        $full = Join-Path $Root $pattern

        # 单层通配（src/*/xxx）用 Resolve-Path 展开
        $matches = @(Get-ChildItem -Path $full -File -ErrorAction SilentlyContinue)
        foreach ($item in $matches) {
            $found.Add($item.FullName.Substring($Root.Length).TrimStart('\', '/'))
        }
    }

    return $found | Sort-Object -Unique
}

$currentFiles = Get-WiringFiles -Root $ProjectPath
$freshFiles = Get-WiringFiles -Root $tempProject

$allNames = @($currentFiles + $freshFiles) | Sort-Object -Unique

$different = [System.Collections.Generic.List[string]]::new()
$missing = [System.Collections.Generic.List[string]]::new()
$added = [System.Collections.Generic.List[string]]::new()

foreach ($name in $allNames) {
    $current = Join-Path $ProjectPath $name
    $fresh = Join-Path $tempProject $name

    if (-not (Test-Path $current)) { $added.Add($name); continue }
    if (-not (Test-Path $fresh)) { $missing.Add($name); continue }

    $a = (Get-Content -LiteralPath $current -Raw)
    $b = (Get-Content -LiteralPath $fresh -Raw)

    if ($a -ne $b) { $different.Add($name) }
}

# ---------- 5. 输出报告 ----------
if (-not $DiffFile) {
    $DiffFile = Join-Path $compareRoot 'diff.txt'
}

$report = [System.Collections.Generic.List[string]]::new()
$report.Add("CloudL 模板比对报告")
$report.Add("业务项目: $ProjectPath")
$report.Add("新模板生成: $tempProject")
$report.Add("框架版本: $FrameworkVersion（模板包 $templateVersion）")
$report.Add('')

if ($different.Count -gt 0) {
    $report.Add("【有差异】$($different.Count) 个文件（新模板的改动可能需要同步）")
    $different | ForEach-Object { $report.Add("  - $_") }
    $report.Add('')
    $report.Add('--- 详细差异 ---')
    $report.Add('')

    foreach ($name in $different) {
        $report.Add("########## $name ##########")
        $gitDiff = git diff --no-index --no-color -- (Join-Path $tempProject $name) (Join-Path $ProjectPath $name) 2>&1
        $report.AddRange([string[]]$gitDiff)
        $report.Add('')
    }
}

if ($missing.Count -gt 0) {
    $report.Add('')
    $report.Add("【新模板已移除 / 你的项目还留着】$($missing.Count) 个文件")
    $missing | ForEach-Object { $report.Add("  - $_") }
}

if ($added.Count -gt 0) {
    $report.Add('')
    $report.Add("【新模板新增（你的项目里没有）】$($added.Count) 个文件")
    $added | ForEach-Object { $report.Add("  - $_") }
}

$report.Add('')
$report.Add('提示：这是「供参考」的差异，不是自动合并。请逐项判断是否需要同步到你的项目。')
$report.Add('     注意：Directory.Packages.props 里的 CloudLVersion 行通常不同（你在项目中可能固定了版本），属正常差异。')
$report.Add('     完整比对（含业务代码）可执行：git diff --no-index <新模板项目> <你的项目>')

$reportText = $report -join [Environment]::NewLine
New-Item -ItemType Directory -Force -Path (Split-Path $DiffFile -Parent) | Out-Null
Set-Content -LiteralPath $DiffFile -Value $reportText -Encoding UTF8

Write-Host ''
Write-Host "==> 装配类文件比对结果" -ForegroundColor Cyan
Write-Host "    有差异: $($different.Count) 个" -ForegroundColor $(if ($different.Count -gt 0) { 'Yellow' } else { 'Green' })
Write-Host "    新模板新增: $($added.Count) 个"
Write-Host "    新模板已移除: $($missing.Count) 个"
Write-Host "    报告已写入: $DiffFile" -ForegroundColor Green

if ($different.Count -eq 0) {
    Write-Host '    装配文件与新模板一致 ✅' -ForegroundColor Green
}
else {
    $different | ForEach-Object { Write-Host "      - $_" -ForegroundColor Yellow }
}
