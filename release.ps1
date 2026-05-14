param(
    [string]$MagickRoot = "",
    [string]$VcpkgRoot = "",
    [switch]$StaticRuntime,
    [switch]$SharedSlint,
    [ValidateSet("Static", "Dynamic")]
    [string]$MagickLinkage = "Static",
    [switch]$FullMagickBuild,
    [switch]$InstallMfc,
    [switch]$UseScoopFallback
)

$ErrorActionPreference = "Stop"
$Repo = Split-Path -Parent $PSCommandPath
$BuildDir = Join-Path $Repo "build\x64\Release"

if (-not $VcpkgRoot) {
    if ($env:VCPKG_ROOT) {
        $VcpkgRoot = $env:VCPKG_ROOT
    } elseif (Test-Path "D:\Scoop\apps\vcpkg\current") {
        $VcpkgRoot = "D:\Scoop\apps\vcpkg\current"
    }
}

if (-not $MagickRoot) {
    $SelfBuilt = Join-Path $Repo "third_party\imagemagick-runtime\x64\Release"
    if (Test-Path (Join-Path $SelfBuilt "include\MagickWand\MagickWand.h")) {
        $MagickRoot = $SelfBuilt
    } elseif ($UseScoopFallback -and (Test-Path "D:\Scoop\apps\imagemagick\current\include\MagickWand\MagickWand.h")) {
        $MagickRoot = "D:\Scoop\apps\imagemagick\current"
        Write-Warning "未发现自编译 ImageMagick，按 -UseScoopFallback 临时使用 Scoop ImageMagick。"
    } else {
        Write-Host "未发现自编译 ImageMagick，开始自动构建 Release $MagickLinkage 运行时..."
        $MagickBuildScript = Join-Path $Repo "scripts\build-magick.ps1"
        $MagickBuildArgs = @{
            Configuration = "Release"
            Arch = "x64"
            Linkage = $MagickLinkage
        }
        if ($FullMagickBuild) {
            $MagickBuildArgs.FullBuild = $true
        }
        if ($InstallMfc) {
            $MagickBuildArgs.InstallMfc = $true
        }

        & $MagickBuildScript @MagickBuildArgs
        if ($LASTEXITCODE -ne 0) {
            throw "ImageMagick 自动构建失败，退出码 $LASTEXITCODE。"
        }
        if (-not (Test-Path (Join-Path $SelfBuilt "include\MagickWand\MagickWand.h"))) {
            throw "ImageMagick 自动构建完成后仍未找到运行时: $SelfBuilt"
        }
        $MagickRoot = $SelfBuilt
    }
}

$ConfigureArgs = @(
    "-S", $Repo,
    "-B", $BuildDir,
    "-G", "Visual Studio 18 2026",
    "-A", "x64"
)
if ($VcpkgRoot) {
    $ConfigureArgs += "-DVCPKG_ROOT=$VcpkgRoot"
}
if ($MagickRoot) {
    $ConfigureArgs += "-DMAGICK_ROOT=$MagickRoot"
}
$ConfigureArgs += "-DAVIF_STATIC_MSVC_RUNTIME=$(if ($StaticRuntime) { 'ON' } else { 'OFF' })"
$ConfigureArgs += "-DAVIF_STATIC_SLINT=$(if ($SharedSlint) { 'OFF' } else { 'ON' })"

cmake @ConfigureArgs
if ($LASTEXITCODE -ne 0) {
    throw "CMake 配置失败，退出码 $LASTEXITCODE。"
}
cmake --build $BuildDir --config Release --parallel
if ($LASTEXITCODE -ne 0) {
    throw "Release 构建失败，退出码 $LASTEXITCODE。"
}

$OutputDir = Join-Path $Repo "bin\x64\Release"
$StaleArtifacts = @(
    "AVIFConsoleCpp.exe",
    "AVIFConsoleCpp.pdb",
    "avif_core.dll",
    "avif_core.pdb"
)
if (-not $SharedSlint) {
    $StaleArtifacts += @("slint_cpp.dll", "slint-compiler.exe", "slint_compiler.pdb")
}
foreach ($Name in $StaleArtifacts) {
    $Path = Join-Path $OutputDir $Name
    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

Write-Host ""
Write-Host "Release 输出:"
Write-Host "  $OutputDir\AVIFConsoleCli.exe"
Write-Host "  $OutputDir\AVIFStudio.exe"
