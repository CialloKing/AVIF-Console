param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [ValidateSet("x64")]
    [string]$Arch = "x64",

    [string]$SourceRoot = "",
    [string]$RuntimeRoot = "",
    [string]$RepositoryUrl = "https://github.com/ImageMagick/Windows.git",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$Repo = Split-Path -Parent (Split-Path -Parent $PSCommandPath)

if (-not $SourceRoot) {
    $SourceRoot = Join-Path $Repo "third_party\imagemagick-src"
}
if (-not $RuntimeRoot) {
    $RuntimeRoot = Join-Path $Repo "third_party\imagemagick-runtime\$Arch\$Configuration"
}

function Find-MSBuild {
    $VsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $VsWhere) {
        $Found = & $VsWhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($Found) {
            return $Found
        }
    }

    $Command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($Command) {
        return $Command.Source
    }
    throw "找不到 MSBuild。请确认 Visual Studio 2026 已安装 C++ 桌面工作负载。"
}

function Invoke-CmdScript([string]$ScriptPath) {
    Push-Location (Split-Path -Parent $ScriptPath)
    try {
        & cmd.exe /c "`"$ScriptPath`""
        if ($LASTEXITCODE -ne 0) {
            throw "$ScriptPath 失败，退出码 $LASTEXITCODE。"
        }
    } finally {
        Pop-Location
    }
}

function Copy-IfExists([string]$Path, [string]$Destination) {
    if (Test-Path $Path) {
        New-Item -ItemType Directory -Force -Path $Destination | Out-Null
        Copy-Item -LiteralPath $Path -Destination $Destination -Force
    }
}

Write-Host "ImageMagick Windows 源码: $SourceRoot"
if (-not (Test-Path (Join-Path $SourceRoot ".git"))) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SourceRoot) | Out-Null
    git clone $RepositoryUrl $SourceRoot
} else {
    git -C $SourceRoot fetch --prune
}
git -C $SourceRoot checkout main
git -C $SourceRoot pull --ff-only

$CloneScript = Join-Path $SourceRoot "clone-repositories-im7.cmd"
if (Test-Path $CloneScript) {
    Write-Host "同步 ImageMagick / delegate 源码..."
    Invoke-CmdScript $CloneScript
} else {
    Write-Warning "未找到 clone-repositories-im7.cmd，跳过 delegate 同步。"
}

if (-not $SkipBuild) {
    $MSBuild = Find-MSBuild
    Write-Host "MSBuild: $MSBuild"

    $KnownSolutions = @(
        Join-Path $SourceRoot "ImageMagick\VisualMagick\IM7.Dynamic.sln",
        Join-Path $SourceRoot "VisualMagick\IM7.Dynamic.sln",
        Join-Path $SourceRoot "IM7.Dynamic.sln",
        Join-Path $SourceRoot "ImageMagick.sln"
    ) | Where-Object { Test-Path $_ }

    if (-not $KnownSolutions) {
        $KnownSolutions = Get-ChildItem -Path $SourceRoot -Recurse -Filter "*.sln" |
            Where-Object { $_.Name -match "IM7|ImageMagick|Dynamic" } |
            Sort-Object FullName |
            ForEach-Object { $_.FullName }
    }

    if (-not $KnownSolutions) {
        throw "没有找到可构建的 ImageMagick 解决方案。请先按 ImageMagick/Windows 文档生成 IM7.Dynamic.sln，然后重新运行本脚本。"
    }

    $Solution = $KnownSolutions | Select-Object -First 1
    Write-Host "构建: $Solution"
    & $MSBuild $Solution /m /restore /p:Configuration=$Configuration /p:Platform=$Arch
    if ($LASTEXITCODE -ne 0) {
        throw "ImageMagick 构建失败，退出码 $LASTEXITCODE。"
    }
}

Write-Host "提取运行时: $RuntimeRoot"
New-Item -ItemType Directory -Force -Path $RuntimeRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $RuntimeRoot "include") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $RuntimeRoot "lib") | Out-Null

$WandDll = Get-ChildItem -Path $SourceRoot -Recurse -Filter "CORE_RL_MagickWand_.dll" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $WandDll) {
    throw "未找到 CORE_RL_MagickWand_.dll，无法提取运行时。"
}
$BinRoot = $WandDll.Directory.FullName

Get-ChildItem -Path $BinRoot -Filter "*.dll" | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $RuntimeRoot -Force
}
Get-ChildItem -Path $BinRoot -Filter "*.exe" | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $RuntimeRoot -Force
}
Get-ChildItem -Path $BinRoot -File | Where-Object {
    $_.Extension -in @(".xml", ".icc", ".txt")
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $RuntimeRoot -Force
}

$LibRoot = Get-ChildItem -Path $SourceRoot -Recurse -Filter "CORE_RL_MagickWand_.lib" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($LibRoot) {
    Get-ChildItem -Path $LibRoot.Directory.FullName -Filter "*.lib" | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $RuntimeRoot "lib") -Force
    }
}

$Header = Get-ChildItem -Path $SourceRoot -Recurse -Filter "MagickWand.h" |
    Where-Object { $_.Directory.Name -eq "MagickWand" } |
    Select-Object -First 1
if (-not $Header) {
    throw "未找到 MagickWand.h，无法提取 include。"
}
$IncludeRoot = Split-Path -Parent $Header.Directory.FullName
foreach ($Dir in @("MagickWand", "MagickCore", "Magick++")) {
    $SourceDir = Join-Path $IncludeRoot $Dir
    if (Test-Path $SourceDir) {
        Copy-Item -LiteralPath $SourceDir -Destination (Join-Path $RuntimeRoot "include") -Recurse -Force
    }
}

$ModuleRoot = Join-Path $BinRoot "modules"
if (-not (Test-Path $ModuleRoot)) {
    $HeicModule = Get-ChildItem -Path $SourceRoot -Recurse -Filter "IM_MOD_RL_heic_.dll" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($HeicModule) {
        $ModuleRoot = Split-Path -Parent (Split-Path -Parent $HeicModule.Directory.FullName)
    }
}
if (Test-Path $ModuleRoot) {
    Copy-Item -LiteralPath $ModuleRoot -Destination $RuntimeRoot -Recurse -Force
} else {
    Write-Warning "未找到 modules 目录。AVIF/WebP coder 可能无法在运行时加载。"
}

foreach ($Notice in @("LICENSE", "LICENSE.txt", "NOTICE", "NOTICE.txt")) {
    $Found = Get-ChildItem -Path $SourceRoot -Recurse -File -Filter $Notice -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($Found) {
        Copy-Item -LiteralPath $Found.FullName -Destination $RuntimeRoot -Force
    }
}

Write-Host ""
Write-Host "ImageMagick runtime 已提取到:"
Write-Host "  $RuntimeRoot"
Write-Host "后续构建:"
Write-Host "  .\release.ps1 -MagickRoot `"$RuntimeRoot`""
