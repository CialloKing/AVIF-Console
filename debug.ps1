param(
    [string]$VcpkgRoot
)

$ErrorActionPreference = "Stop"
$Repo = $PSScriptRoot
$Solution = Join-Path $Repo "图片avif压缩控制台.slnx"

function Resolve-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $install = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($install) {
            $candidate = Join-Path $install "MSBuild\Current\Bin\amd64\MSBuild.exe"
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }
    $fromPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }
    throw "MSBuild.exe not found. 请确认 VS 2026 已安装 C++ 桌面开发组件。"
}

function Resolve-VcpkgRoot {
    param([string]$ExplicitRoot)
    if ($ExplicitRoot) {
        return (Resolve-Path $ExplicitRoot).Path
    }
    if ($env:VCPKG_ROOT -and (Test-Path $env:VCPKG_ROOT)) {
        return (Resolve-Path $env:VCPKG_ROOT).Path
    }
    $scoopRoot = "D:\Scoop\apps\vcpkg\current"
    if (Test-Path $scoopRoot) {
        return (Resolve-Path $scoopRoot).Path
    }
    throw "vcpkg root not found. 可用 -VcpkgRoot 指定，例如 .\debug.ps1 -VcpkgRoot D:\Scoop\apps\vcpkg\current"
}

$MSBuild = Resolve-MSBuild
$Vcpkg = Resolve-VcpkgRoot $VcpkgRoot
if (-not $Vcpkg.EndsWith("\")) {
    $Vcpkg += "\"
}

Write-Host "MSBuild: $MSBuild"
Write-Host "vcpkg:   $Vcpkg"
& $MSBuild $Solution /m /v:minimal /p:Configuration=Debug /p:Platform=x64 /p:VcpkgRoot="$Vcpkg"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "输出: $Repo\bin\x64\Debug\AVIFConsoleCpp.exe"
