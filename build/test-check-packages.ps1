#Requires -Version 7.0
<#
.SYNOPSIS
    包安检规则自身的回归测试。

.DESCRIPTION
    安检规则最大的风险不是误报，而是<strong>静默失效</strong>：规则写错时它会放行一切，且不会有任何提示。
    因此这里用三类假包分别验证三个方向，互不掩盖：

      1. 内容规则：普通文件名里出现真凭据形态 → 必须拦截
      2. 文件名规则：appsettings*.json → 必须拦截
      3. 不误报：文档只是提到 client_secret / password 这类词 → 必须放行

    由 ci.yml 与 release.yml 调用。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$checkScript = Join-Path $PSScriptRoot 'check-packages.ps1'

try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }

$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ('cloudl-pkg-selftest-' + [guid]::NewGuid().ToString('N'))

function New-FakePackage {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][hashtable]$Files
    )

    $contentDir = Join-Path $sandbox ('content-' + $Name)
    New-Item -ItemType Directory -Path $contentDir -Force | Out-Null

    foreach ($fileName in $Files.Keys) {
        Set-Content -LiteralPath (Join-Path $contentDir $fileName) -Value $Files[$fileName] -Encoding UTF8
    }

    $feedDir = Join-Path $sandbox ('feed-' + $Name)
    New-Item -ItemType Directory -Path $feedDir -Force | Out-Null
    [System.IO.Compression.ZipFile]::CreateFromDirectory($contentDir, (Join-Path $feedDir ('Fake.' + $Name + '.1.0.0.nupkg')))

    return $feedDir
}

function Test-Guard {
    param(
        [Parameter(Mandatory)][string]$FeedPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [Parameter(Mandatory)][string]$Label
    )

    $output = & pwsh -NoProfile -File $checkScript -FeedPath $FeedPath 2>&1
    $exitCode = $LASTEXITCODE
    $passed = ($exitCode -eq 0)
    $ok = ($passed -eq $ShouldPass)

    $expected = if ($ShouldPass) { '放行' } else { '拦截' }
    $mark = if ($ok) { '[OK]  ' } else { '[FAIL]' }
    Write-Host "$mark $Label（期望$expected，实际 exit=$exitCode）"

    if (-not $ok) {
        $output | Select-Object -Last 6 | ForEach-Object { Write-Host "        $_" }
    }

    return $ok
}

try {
    $results = @()

    $results += Test-Guard `
        -FeedPath (New-FakePackage -Name 'Content' -Files @{ 'notes.txt' = 'Connection: Server=db;User Id=sa;Password=SuperSecret123456;' }) `
        -ShouldPass $false `
        -Label '内容规则：真凭据必须被拦截'

    $results += Test-Guard `
        -FeedPath (New-FakePackage -Name 'FileName' -Files @{ 'appsettings.Production.json' = '{"Logging":{"LogLevel":"Information"}}' }) `
        -ShouldPass $false `
        -Label '文件名规则：appsettings*.json 必须被拦截'

    $results += Test-Guard `
        -FeedPath (New-FakePackage -Name 'Docs' -Files @{ 'README.md' = '文档里提到 client_secret、access_token、Password 这些词是正常的。' }) `
        -ShouldPass $true `
        -Label '不误报：文档提词必须放行'

    if ($results -contains $false) {
        throw '包安检规则自测未通过。'
    }

    Write-Host '包安检规则自测通过 ✅（拦截真凭据、拦截危险文件、不误报文档）' -ForegroundColor Green
}
finally {
    Remove-Item -Path $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
