# ImageMagick 运行时构建与打包

项目运行时直接链接 `MagickWand`。仓库不提交 ImageMagick 二进制，统一由脚本生成本地 runtime/development bundle。

## 构建命令

```powershell
.\scripts\build-magick.ps1 -Configuration Release -Arch x64
```

脚本默认使用 `-Linkage Static`，先构建 ImageMagick 官方 `Configure.exe`，生成 `IM7.Static.x64.sln`，再优先只编译 MagickCore/MagickWand 与 AVIF(WebP/HEIC)、WebP coder。需要完整输入格式支持时加 `-FullBuild`；如果静态 delegate 链接不顺，可改用：

```powershell
.\scripts\build-magick.ps1 -Configuration Release -Arch x64 -Linkage Dynamic -FullBuild
```

默认输出：

```text
third_party\imagemagick-runtime\x64\Release
```

该目录按实际链接方式包含：

- `include\MagickWand` / `include\MagickCore`
- `lib\CORE_RL_MagickWand_.lib`
- `lib\CORE_RL_MagickCore_.lib`
- 静态构建时的 `CORE_RL_*.lib` coder/delegate libs
- 动态构建时的 `CORE_RL_*.dll`、`modules\coders\IM_MOD_RL_heic_.dll`、`IM_MOD_RL_webp_.dll`
- `configure.xml`、`delegates.xml`、`policy.xml` 等配置
- `License` / `NOTICE`

## CMake 使用

默认构建脚本会优先寻找：

```text
third_party\imagemagick-runtime\x64\Release
```

也可以显式指定：

```powershell
.\release.ps1 -MagickRoot ".\third_party\imagemagick-runtime\x64\Release"
```

构建完成后，CMake 会把存在的运行时 DLL、modules 和配置文件复制到 `AVIFConsoleCli.exe` / `AVIFStudio.exe` 所在目录。静态 ImageMagick 构建没有对应 DLL 时，不会额外复制。程序启动时会把这些路径设置到：

- `MAGICK_HOME`
- `MAGICK_CONFIGURE_PATH`
- `MAGICK_CODER_MODULE_PATH`
- `MAGICK_FILTER_MODULE_PATH`

## 本地 fallback

`release.ps1` 默认不会再悄悄使用 Scoop。没有自编译 runtime 时，它会自动运行 `scripts\build-magick.ps1`。如果只是本机临时调试，并且本机存在：

```text
D:\Scoop\apps\imagemagick\current
```

可以显式传入：

```powershell
.\release.ps1 -UseScoopFallback
```

`debug.ps1` 仍允许使用 Scoop fallback，方便快速开发；发布前应以 `release.ps1` 自动生成的自编译 runtime 为准。

## 验证

构建后可以运行：

```powershell
.\bin\x64\Release\AVIFConsoleCli.exe -i input -o Avifoutput -q q90
```

动态构建时，如果想确认 AVIF/WebP coder 是否随 runtime 一起复制，可以检查：

```powershell
Get-ChildItem .\bin\x64\Release\modules\coders\IM_MOD_RL_*heic*.dll
Get-ChildItem .\bin\x64\Release\modules\coders\IM_MOD_RL_*webp*.dll
```

## 单文件分发

CMake 默认静态链接 Slint，因此 UI 不再需要 `slint_cpp.dll`。真正单 exe 还要求 ImageMagick、AVIF/WebP delegate、scnlib 和 CRT 都能静态链接；推荐顺序是：

```powershell
.\scripts\build-magick.ps1 -Configuration Release -Arch x64 -Linkage Static
.\release.ps1 -MagickRoot ".\third_party\imagemagick-runtime\x64\Release" -StaticRuntime
```

如果静态 delegate 或 CRT 与本机 vcpkg triplet 不兼容，保留少量 DLL 是更稳妥的发布方式。
