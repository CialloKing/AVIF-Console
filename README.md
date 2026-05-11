# AVIF Console C++23

这是原 C# AVIF 压缩控制台的 C++23 重写版。当前默认后端是 ImageMagick `magick.exe`，仓库内已带精简运行时，优先使用 `vendor/imagemagick`，找不到时才回退到 `AVIF_MAGICK` 或系统 `PATH`。

## 环境

- Windows + Visual Studio 2026 C++ 桌面开发组件
- vcpkg 已安装 `scnlib:x64-windows`
- 可选：系统已安装 ImageMagick。仓库默认自带运行时，不强制要求本机安装

当前工程默认 vcpkg 路径为 `D:\Scoop\apps\vcpkg\current\`。如果你的路径不同，构建脚本支持传入 `-VcpkgRoot`。

## 编译

Debug:

```powershell
.\debug.ps1
```

Release:

```powershell
.\release.ps1
```

直接使用 MSBuild:

```powershell
msbuild .\图片avif压缩控制台.slnx /m /p:Configuration=Release /p:Platform=x64 /p:VcpkgRoot="D:\Scoop\apps\vcpkg\current\"
```

输出位置:

- Debug: `bin\x64\Debug\AVIFConsoleCpp.exe`
- Release: `bin\x64\Release\AVIFConsoleCpp.exe`

## 使用

默认质量是 `q90`，默认不传 `heic:speed`，让 ImageMagick 使用自身默认速度参数。
如果输出文件重名，程序会按扫描顺序依次覆盖；默认模板 `covers-{index}.avif` 会避免重名。

```powershell
.\bin\x64\Release\AVIFConsoleCpp.exe -i input -o Avifoutput
```

常用参数:

- `-i, --input <dir>`: 输入目录，默认 `input`
- `-o, --output <dir>`: 输出目录，默认 `Avifoutput`
- `-q, --quality <1-100>`: ImageMagick 质量，默认 `90`。也接受 `q90` 或 `0.9`
- `-p, --preset fast|balanced|best|extreme`: 预设，默认 `best`
- `-t, --threads <n>`: 并发数量，默认 CPU 线程数。也支持 `-j/--jobs`
- `-m, --template <模板>`: 输出文件名模板，默认 `covers-{index}.avif`
- `--max-resolution <px>`: 限制最长边；`0` 表示不缩放，默认 `0`
- `--speed <0-8>`: 可选，显式传给 ImageMagick `heic:speed`
- `--define <key=value>`: 额外传给 `magick -define`，可重复
- `--backend magick`: 后端占位参数，当前仅支持 `magick`
- `--magick <path>`: 指定外部 `magick.exe` 或 ImageMagick 目录
- `--timeout-encode <minutes>`: 单张图片编码超时，默认 `30`
- `--strip`: 去除元数据
- `--skip-existing`: 已有输出时跳过
- `--overwrite`: 覆盖已有输出，默认行为

示例:

```powershell
.\bin\x64\Release\AVIFConsoleCpp.exe -i "D:\图片" -o Avifoutput -q q90
.\bin\x64\Release\AVIFConsoleCpp.exe -i pngs -o out --max-resolution 2560 --strip
.\bin\x64\Release\AVIFConsoleCpp.exe -i input -o out --define heic:chroma=444
```

运行后会生成:

- `Avifoutput\*.avif`
- `Avifoutput\summary.csv`
- `Avifoutput\log\avif-console.log`

## 代码结构

- `src\main.cpp`: Windows 控制台 UTF-8 初始化、异常兜底、启动流水线
- `src\app\config.ixx`: 参数、预设、帮助文本，数值输入使用 scnlib
- `src\core\process.ixx`: Win32 进程封装、路径编码、图片扫描、日志、CSV
- `src\backends\magick_backend.ixx`: ImageMagick 运行时解析与 AVIF 编码参数
- `src\app\pipeline.ixx`: 多线程任务调度、进度输出、汇总
- `vendor\imagemagick`: 精简 ImageMagick 7.1.2-21 Q16-HDRI x64 运行时
- `debug.ps1` / `release.ps1`: 本地 VS 2026 编译脚本
- `docs\cpp-port.md`: C# 到 C++ 的翻译说明

实现要点:

- 输出统一使用 C++23 `<print>` 的 `std::print` / `std::println`
- 批处理线程使用 `std::jthread`，线程退出自动 join
- 程序入口用 `std::expected<T, std::string>` 包装参数解析和流水线执行
- 单文件处理内部有异常兜底，某张图片失败不会终止整个批处理
- 默认后端不再依赖 ffmpeg/ffprobe，普通用户可直接运行仓库内置的 Magick 后端
