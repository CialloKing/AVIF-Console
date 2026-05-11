# C++23 翻译说明

## 目标

本分支把原来的 C# 控制台翻译为现代 C++23 版本。重点不是逐行照搬，而是保留批量转码的主要工作流，同时降低后端复杂度，让代码更容易阅读、调试和继续扩展。

## 当前后端

当前默认后端是 ImageMagick `magick.exe`，而不是 ffmpeg。仓库内置了精简运行时：

1. 优先查找 `vendor/imagemagick/magick.exe`
2. 再查找环境变量 `AVIF_MAGICK`
3. 最后回退到系统 `PATH` 中的 `magick.exe`

默认编码参数尽量贴近 ImageMagick 本身：

- 默认质量：`-quality 90`
- 默认速度：不传 `heic:speed`，使用 ImageMagick 默认值
- 默认方向：`-auto-orient`
- 默认不缩放，`--max-resolution` 显式设置后才缩放

## 保留的功能

- 批量扫描 `jpg/jpeg/png/webp/bmp/tif/tiff/gif/jxl/jp2/heic/heif`
- 输出名模板 `{name}` / `{index}` / `{ext}`，默认 `covers-{index}.avif`
- `fast / balanced / best / extreme` 预设
- `q90` 风格质量参数
- 并行处理，处理时优先调度大文件
- 重名输出按扫描顺序覆盖，最终保留同名组中的最后一张
- 日志与 `summary.csv`
- 单张失败继续处理后续图片

## 简化的部分

- 移除了 ffmpeg/ffprobe 后端、CRF 搜索和 SSIM 测量。
- 质量参数改为 ImageMagick 的 `-quality`，语义更直接。
- 不再默认缩放到固定长边，避免用户误以为编码质量差其实是分辨率被改动。
- 运行时依赖被收敛到 `vendor/imagemagick`，普通运行不需要额外安装 ffmpeg。

## 模块划分

- `avif.config`: 参数结构、预设、帮助文本、命令行解析。数值解析使用 vcpkg 的 `scnlib`。
- `avif.core`: 进程运行器、UTF-8/宽字符转换、图片扫描、日志、CSV。
- `avif.magick_backend`: Magick 运行时解析、环境变量配置、AVIF 编码命令构造。
- `avif.pipeline`: 多线程调度、进度输出和汇总。
- `main.cpp`: Windows 控制台 UTF-8 初始化与异常兜底。

## scnlib

用户输入的数值参数通过 `scn::scan_value` 解析，例如质量、并发数量、最长边和超时时间。这样可以避免 `std::stoi`/`std::stod` 分散在解析逻辑里，也符合“输入库为 scn”的要求。

## 进阶改动

- 输出路径统一使用 `<print>` 的 `std::print` / `std::println`。
- 批处理线程使用 `std::jthread`，并在 worker 内部保护单文件异常，防止线程异常导致程序终止。
- 程序入口使用 `std::expected<T, std::string>` 包装参数解析与流水线执行。
- 工程保留 vcpkg include/lib 配置，可继续引入 Boost 等第三方库。

## 后续可扩展点

- 如果未来需要更精细的体积目标，可以在 `avif.magick_backend` 上增加质量二分搜索。
- 可以增加新的后端模块，例如 `avif.ffmpeg_backend` 或 `avif.libheif_backend`，流水线层不需要大改。
- 可以增加 manifest 模式的 vcpkg 配置，减少本机路径差异。
