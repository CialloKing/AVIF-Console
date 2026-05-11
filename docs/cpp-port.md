# C++23 翻译说明

## 目标

本分支把原来的单文件 C# 控制台翻译为现代 C++23 版本。重点不是逐行照搬，而是保留主要行为，同时压缩复杂度，让代码更容易阅读、调试和继续扩展。

## 保留的功能

- 批量扫描 `jpg/jpeg/png/webp/bmp/tif/tiff`
- 自然排序后按原顺序编号，处理时优先处理大文件
- `fast / balanced / best / extreme` 预设
- 手动 CRF 与 CRF 二分搜索
- 420 / 422 / 444、8/10 bit、源自适应
- 无损模式
- 输出名模板 `{name}` / `{index}`
- ffmpeg / ffprobe 自动查找
- 并行处理
- 日志与 `avif_stats.csv`

## 简化的部分

- 搜索指标统一为 SSIM，避免把 VMAF、PSNR、MS-SSIM 的解析和依赖一起搬入 C++ 版。
- 去掉内存缓存层。C++ 版每次运行以本次结果为准，行为更直接。
- 去掉多级安全扫描状态机，只保留一次像素格式回退：失败时尝试 `yuv420p` 或 `yuva420p`。
- 无参数时显示帮助，不进入复杂交互模式。

## 模块划分

- `avif.config`: 参数结构、预设、帮助文本、命令行解析。数值解析使用 vcpkg 的 `scnlib`。
- `avif.process`: 进程运行器、ffprobe 探测、ffmpeg 编码、SSIM 计算、并发调度、CSV 导出。
- `main.cpp`: Windows 控制台 UTF-8 初始化与异常兜底。

## scnlib

用户输入的数值参数通过 `scn::scan_value` 解析，例如线程数、CRF、SSIM 目标、位深和超时时间。这样可以避免 `std::stoi`/`std::stod` 分散在解析逻辑里，也符合“输入库为 scn”的要求。

## 进阶改动

- 输出路径统一使用 `<print>` 的 `std::print` / `std::println`。
- 批处理线程使用 `std::jthread`，并在 worker 内部保护单文件异常，防止线程异常导致程序终止。
- 程序入口使用 `std::expected<T, std::string>` 包装参数解析与流水线执行；模块内部的文件系统和单文件处理路径也增加了异常兜底。
- 工程保留 vcpkg include/lib 配置，可继续引入 Boost 等第三方库。

## 后续可扩展点

- 增加 VMAF/PSNR/MS-SSIM 模块，做成独立 metric module。
- 为硬件编码器补充分支参数。
- 把日志列和 CSV schema 做成可配置。
- 增加 manifest 模式的 vcpkg 配置，减少本机路径差异。
