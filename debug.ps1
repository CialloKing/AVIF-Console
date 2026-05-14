param(
    [string]$MagickRoot = "",
    [string]$VcpkgRoot = ""
)

$ErrorActionPreference = "Stop"
$Repo = Split-Path -Parent $PSCommandPath
$BuildDir = Join-Path $Repo "build\x64\Debug"

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
    } elseif (Test-Path "D:\Scoop\apps\imagemagick\current\include\MagickWand\MagickWand.h") {
        $MagickRoot = "D:\Scoop\apps\imagemagick\current"
        Write-Warning "未发现自编译 ImageMagick，Debug 构建临时使用 Scoop ImageMagick。"
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

cmake @ConfigureArgs
if ($LASTEXITCODE -ne 0) {
    throw "CMake 配置失败，退出码 $LASTEXITCODE。"
}
cmake --build $BuildDir --config Debug --parallel
if ($LASTEXITCODE -ne 0) {
    throw "Debug 构建失败，退出码 $LASTEXITCODE。"
}

Write-Host ""
Write-Host "Debug 输出:"
Write-Host "  $Repo\bin\x64\Debug\AVIFConsoleCli.exe"
Write-Host "  $Repo\bin\x64\Debug\AVIFStudio.exe"
