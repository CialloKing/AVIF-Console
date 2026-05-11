# AVIF Console C++23

这是原 C# AVIF 压缩控制台的 C++23 版本。新版本保留批量扫描、预设、CRF、SSIM 搜索、输出命名、日志、CSV 统计和 ffmpeg/ffprobe 调用，并把代码拆成 C++ modules，主入口保持很薄。

## 环境

- Windows + Visual Studio 2026 C++ 桌面开发组件
- vcpkg 已安装 `scnlib:x64-windows`
- scoop 已安装 `ffmpeg`，并且 `ffmpeg.exe` / `ffprobe.exe` 在 `PATH` 中

当前工程默认 vcpkg 路径为 `D:\Scoop\apps\vcpkg\current\`。如果你的路径不同，构建脚本支持传入 `-VcpkgRoot`。

工程已预留 vcpkg include/lib 路径。后续如果要引入 Boost 或其他库，可以直接通过 vcpkg 安装对应的 `x64-windows` 包，再在 `.vcxproj` 中追加库名。

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

```powershell
.\bin\x64\Release\AVIFConsoleCpp.exe -i input -o Avifoutput -p best -s -q 0.98
```

常用参数:

- `-i <dir>`: 输入目录，默认 `input`
- `-o <dir>`: 输出目录，默认 `Avifoutput`
- `-p fast|balanced|best|extreme`: 预设，默认 `extreme`
- `-s`: 启用 CRF 搜索
- `-n`: 禁用 CRF 搜索
- `-q <0..1>`: SSIM 搜索目标
- `-r <crf>`: 手动 CRF，配合 `-n`
- `-r <min:max>`: 搜索范围，配合 `-s`
- `-c` / `-g` / `-f`: 强制 420 / 422 / 444
- `-a`: 源自适应
- `-d 8|10`: 指定位深
- `-t <n>`: 并行线程数
- `-m <模板>`: 输出文件名模板，支持 `{name}` 和 `{index}`
- `--encoder <name>`: 指定 ffmpeg AV1 编码器
- `--max-resolution <px>`: 输出缩放长边上限，`0` 为禁用
- `--output-full-res`: 最终输出保持原尺寸

示例:

```powershell
.\bin\x64\Release\AVIFConsoleCpp.exe -i pics -o avifs -n -r 32 -c -d 8
.\bin\x64\Release\AVIFConsoleCpp.exe -i pngs -o out -l -m {name}.avif
.\bin\x64\Release\AVIFConsoleCpp.exe -i input -o out --encoder libsvtav1 -p balanced
```

运行后会生成:

- `Avifoutput\*.avif`
- `Avifoutput\avif_stats.csv`
- `Avifoutput\log\run.log`
- `Avifoutput\log\crf_search.log`
- `Avifoutput\log\error.log`

## 代码结构

- `src\main.cpp`: 控制台编码初始化、参数解析、启动流水线
- `src\avif_config.ixx`: 参数、预设、帮助文本，数值输入使用 scnlib
- `src\avif_process.ixx`: ffmpeg/ffprobe 调用、图片扫描、编码、SSIM、CSV、多线程调度
- `debug.ps1` / `release.ps1`: 本地 VS 2026 编译脚本
- `docs\cpp-port.md`: C# 到 C++ 的翻译说明

实现要点:

- 输出统一使用 C++23 `<print>` 的 `std::print` / `std::println`
- 批处理线程使用 `std::jthread`，线程退出自动 join
- 程序入口用 `std::expected<T, std::string>` 包装参数解析和流水线执行，避免异常穿透导致闪退
- 单文件处理内部有异常兜底，某张图片失败不会终止整个批处理

## 取舍

C# 版里包含较重的缓存、多指标 VMAF/PSNR/MS-SSIM、编码器可用性压力测试和多级安全扫描。C++ 版按“可读、可维护、能直接编译运行”重写，保留日常压缩最常用的路径，并把搜索指标收敛为 SSIM。
