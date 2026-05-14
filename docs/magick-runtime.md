# ImageMagick 运行时构建与打包

项目运行时直接链接 `MagickWand`，因此发布目录需要同时带上 ImageMagick DLL、coder/filter modules 和配置文件。仓库不提交这些二进制，统一由脚本生成。

## 构建命令

```powershell
.\scripts\build-magick.ps1 -Configuration Release -Arch x64
```

默认输出：

```text
third_party\imagemagick-runtime\x64\Release
```

该目录包含：

- `include\MagickWand` / `include\MagickCore`
- `lib\CORE_RL_MagickWand_.lib`
- `lib\CORE_RL_MagickCore_.lib`
- `CORE_RL_*.dll`
- `modules\coders\IM_MOD_RL_heic_.dll`
- `modules\coders\IM_MOD_RL_webp_.dll`
- `modules\filters`
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

构建完成后，CMake 会把运行时 DLL、modules 和配置文件复制到 `AVIFConsoleCli.exe` / `AVIFStudio.exe` 所在目录。程序启动时会把这些路径设置到：

- `MAGICK_HOME`
- `MAGICK_CONFIGURE_PATH`
- `MAGICK_CODER_MODULE_PATH`
- `MAGICK_FILTER_MODULE_PATH`

## 本地 fallback

如果还没有自编译运行时，但本机存在：

```text
D:\Scoop\apps\imagemagick\current
```

`debug.ps1` / `release.ps1` 会用它完成本地构建，并打印 warning。这个 fallback 只是为了开发方便，发布前仍建议跑 `scripts\build-magick.ps1`。

## 验证

构建后可以运行：

```powershell
.\bin\x64\Release\AVIFConsoleCli.exe -i input -o Avifoutput -q q90
```

如果想确认 AVIF/WebP coder 是否随 runtime 一起复制，可以检查：

```powershell
Get-ChildItem .\bin\x64\Release\modules\coders\IM_MOD_RL_*heic*.dll
Get-ChildItem .\bin\x64\Release\modules\coders\IM_MOD_RL_*webp*.dll
```
