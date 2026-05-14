# AVIF Studio / AVIF Console C++23

这是原 C# AVIF 压缩控制台的 C++23 迁移版。项目现在以 CMake 为主构建系统，核心转换逻辑同时服务于两个入口：

- `AVIFStudio.exe`: Slint 桌面 UI
- `AVIFConsoleCli.exe`: 保留原命令行批处理能力

默认后端已经从调用 `magick.exe` 进程改为直接链接 ImageMagick `MagickWand` API。仓库不再提交 ImageMagick 二进制；需要可分发运行时时，使用 `scripts\build-magick.ps1` 拉取并构建 ImageMagick Windows 源码。

## 环境

- Windows + Visual Studio 2026 C++ 桌面开发组件
- CMake 3.30+
- Rust/Cargo，用于 Slint C++ 后端构建
- Git，用于拉取 Slint 和 ImageMagick 源码
- vcpkg 已安装 `scnlib:x64-windows`

默认 vcpkg 路径按 `VCPKG_ROOT` 或 `D:\Scoop\apps\vcpkg\current` 查找；路径不同可传 `-VcpkgRoot`。

## 自编译 ImageMagick

推荐先构建 ImageMagick 运行时：

```powershell
.\scripts\build-magick.ps1 -Configuration Release -Arch x64
```

脚本会拉取 `https://github.com/ImageMagick/Windows`，先构建官方 `Configure.exe`，再生成并编译 IM7 方案。默认 `-Linkage Static`，只构建 MagickCore/MagickWand 与 AVIF/WebP coder，尽量减少分发 DLL；如果需要完整格式支持可加 `-FullBuild`，如果静态 delegate 链接不顺可改用 `-Linkage Dynamic`。

构建产物会把 headers、libs、可能存在的 DLL/modules、配置 XML 和许可文件提取到：

```text
third_party\imagemagick-runtime\x64\Release
```

该目录被 `.gitignore` 忽略，不提交进仓库。若本机暂时没有自编译运行时，构建脚本会用 Scoop 的 ImageMagick 作为本地开发 fallback，并给出警告。

## 编译

Debug:

```powershell
.\debug.ps1
```

Release:

```powershell
.\release.ps1
```

`release.ps1` 会优先使用 `third_party\imagemagick-runtime\x64\Release`。如果该目录不存在，会自动调用 `scripts\build-magick.ps1 -Configuration Release -Arch x64 -Linkage Static` 构建自编译 ImageMagick。需要完整 ImageMagick 输入格式支持时可加 `-FullMagickBuild`；只是本机快速调试、允许临时使用 Scoop 时才传 `-UseScoopFallback`。

默认静态链接 Slint，因此 `AVIFStudio.exe` 不再需要单独的 `slint_cpp.dll`。如需调试 Slint 共享库，可传 `-SharedSlint`；如确认全部依赖允许静态 CRT，可传 `-StaticRuntime` 进一步减少 VC 运行库依赖。

显式指定运行时：

```powershell
.\release.ps1 -MagickRoot ".\third_party\imagemagick-runtime\x64\Release"
```

输出位置：

- `bin\x64\Debug\AVIFConsoleCli.exe`
- `bin\x64\Debug\AVIFStudio.exe`
- `bin\x64\Release\AVIFConsoleCli.exe`
- `bin\x64\Release\AVIFStudio.exe`

## 使用

启动 UI：

```powershell
.\bin\x64\Release\AVIFStudio.exe
```

CLI 示例：

```powershell
.\bin\x64\Release\AVIFConsoleCli.exe -i input -o Avifoutput
.\bin\x64\Release\AVIFConsoleCli.exe -i "D:\图片" -o Avifoutput -q q90
.\bin\x64\Release\AVIFConsoleCli.exe -i pngs -o out --max-resolution 2560 --strip
.\bin\x64\Release\AVIFConsoleCli.exe -i input -o out --define heic:chroma=444
.\bin\x64\Release\AVIFConsoleCli.exe -i photo.png --format webp --collision random
```

默认质量是 `q90`。默认不设置 `heic:speed`，让 ImageMagick 使用自身默认速度参数；只有显式传入 `--speed 0..8` 时才会设置 `heic:speed`。

如果输出文件重名，默认覆盖。也可以用 `--collision skip|time|random` 跳过或追加后缀；覆盖模式下批处理会按扫描顺序处理同名输出，最后写入的文件保留。`summary.csv` 和日志默认不生成，只有传 `--summary` / `--log` 或在 UI 中勾选时才写入。

常用 CLI 参数：

- `-i, --input <path>`: 输入文件或目录，默认 `input`
- `-o, --output <dir>`: 输出目录，默认与输入同目录
- `-f, --format avif|webp`: 输出格式，默认 `avif`
- `-q, --quality <1-100>`: ImageMagick 质量，默认 `90`，也接受 `q90` 或 `0.9`
- `-p, --preset fast|balanced|best|extreme`: 预设，默认 `best`
- `-t, --threads <n>`: 并发数量，默认 CPU 线程数
- `-m, --template <模板>`: 输出文件名模板，默认 `{name}`，扩展名由 `--format` 决定
- `--max-resolution <px>`: 限制最长边；`0` 表示不缩放
- `--speed <0-8>`: 可选，映射到 `MagickSetOption("heic:speed", value)`
- `--define <key=value>`: 额外映射到 `MagickSetOption(key, value)`，可重复
- `--magick <path>`: 指定 ImageMagick 运行时目录，或其目录中的文件路径
- `--strip`: 去除 EXIF/ICC 等元数据
- `--skip-existing`: 已有输出时跳过
- `--overwrite`: 覆盖已有输出，默认行为
- `--suffix-time` / `--suffix-random`: 输出名追加时间或随机后缀
- `--summary` / `--log`: 可选生成 `summary.csv` 或 `log\avif-console.log`

命名模板支持 `{index}`、`{name}`、`{ext}`、`{date}`、`{time}`、`{datetime}`、`{unix}`、`{rand}`、`{hash}`、`{hash8}`。

## 项目结构

- `CMakeLists.txt`: CMake 主构建，拉取 Slint，链接 scnlib 和 MagickWand
- `scripts\build-magick.ps1`: 拉取、构建并提取 ImageMagick Windows 运行时
- `src\cli\main.cpp`: CLI 入口，控制台 UTF-8 初始化和异常兜底
- `src\ui\main.cpp`: Slint UI 入口，文件/文件夹选择、后台转换、取消和打开目录
- `ui\avif_studio.slint`: 桌面 UI
- `src\app\config.ixx`: 参数、预设、帮助文本，数值输入使用 scnlib
- `src\core\process.ixx`: 路径编码、图片扫描、日志、CSV 和少量 Win32 工具
- `src\backends\magick_backend.ixx`: MagickWand 运行时解析与 AVIF/WebP 编码
- `src\app\pipeline.ixx`: `run_batch` 批处理服务、多线程调度、进度回调、汇总
- `docs\cpp-port.md`: C# 到 C++ 的迁移说明
- `docs\magick-runtime.md`: ImageMagick 运行时构建与打包说明

## 实现要点

- 输出统一使用 C++23 `<print>` 的 `std::print` / `std::println`
- 输入解析使用 vcpkg 中的 `scnlib`
- 批处理线程使用 `std::jthread`
- 错误路径使用 `std::expected<T, std::string>` 和异常兜底，单文件失败不会终止整批
- UI 通过 Slint event loop 投递后台线程进度，转换期间不会阻塞界面
- Slint 默认静态链接；ImageMagick 可用静态脚本路径尽量减少 DLL，运行时不依赖 PATH 中的 `magick.exe`
