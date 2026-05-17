# AVIF 编码器 —— 全自动质量搜索与批量转换工具

**本项目99%的内容由AI完成，如遇到bug可提交反馈，会尽快修复**

一个基于 FFmpeg 的 AV1/AVIF 编码工具，提供 **命令行 (CLI)** 和 **图形界面 (GUI)** 两种交互方式。
自动搜索最佳 CRF 以达到目标质量（VMAF / SSIM / PSNR-Y / MS-SSIM / Mix），支持多编码器、多指标评估、进度跟踪和 CSV 报告导出。

## 特性

- **多编码器支持**
  libaom‑av1、libsvtav1、librav1e、av1_nvenc、av1_qsv、av1_amf、av1_vaapi 等，启动时自动检测可用性。

- **智能 CRF 搜索**
  二分搜索 + 数据驱动的先验中位数初始化 + 哨兵探测，最大化质量的同时控制文件大小。

- **全指标质量评估**
  一次编码即可计算 VMAF、SSIM、PSNR‑Y、MS‑SSIM 以及加权混合评分 (Mix Score)。

- **自适应像素格式与位深**
  根据源文件自动选择最佳色彩采样和位深，保留 Alpha 通道（软件编码器）。

- **安全回退与极限压缩**
  搜索失败时可切换安全模式（yuv420p + cpu‑used 0）最终编码；支持 `--serial-encode` 单线程极限压缩。

- **批量处理与子目录递归**
  按文件大小降序处理，保持目录结构，可选递归子文件夹。

- **输出冲突策略**
  自动重命名、覆盖或跳过已存在的输出文件。

- **预缩放与保留原图**
  可设置最大分辨率缩放输入图像，且可仅用于搜索加速而最终输出原始尺寸。

- **缓存机制**
  编码缓存和指标缓存，避免重复计算，显著加快后续运行。

- **详细的统计与日志**
  控制台实时进度、ETA、最终汇总以及 `avif_stats.csv` 导出。

- **临时文件自动清理**
  无论程序正常结束还是异常退出，临时目录和缓存文件都会被清理（`finally` + `Dispose`）。

## 系统要求

- **操作系统**：Windows 10/11（优先支持长路径）、Linux（推荐）
- **.NET 运行环境**：.NET 10.0 
- **外部依赖**：
  - [FFmpeg](https://ffmpeg.org/) （需包含 AV1 编码器及 libvmaf 滤镜），请确保 `ffmpeg` 和 `ffprobe` 在 `PATH` 中或位于程序同目录。
  - 推荐使用支持 `libvmaf` 的 FFmpeg 构建，否则指标计算会回退到基础 SSIM。




## 命令行用法

```
AVIF 编码器 —— Linux 风格 CLI 命令行工具

用法:
  AvifEncoder --input <目录> --output <目录> [选项]
  AvifEncoder -i <目录> -o <目录> [选项]

主要选项:
  -i, --input <目录>           输入目录 (默认: input)
  -o, --output <目录>          输出目录 (默认: Avifoutput)
  -p, --preset <预设>          预设模式: fast, balanced, best, extreme (默认: extreme)
  -e, --encoder <名称>         指定 AV1 编码器 (默认: libaom-av1)
  -j, --jobs <数量>            并行任务数 (默认: 根据 CPU 自动计算)

质量控制:
  -s, --search                 启用 CRF 搜索 (默认启用)
      --no-search              禁用 CRF 搜索
      --metric <模式>          质量评价模式: vmaf, ssim, psnr, msssim, mix (默认 vmaf)
      --target-vmaf <0-100>    直接设置 VMAF 目标，自动切换模式
      --target-ssim <0-1>      直接设置 SSIM 目标
      --target-psnr <dB>       直接设置 PSNR-Y 目标 (典型 30-50)
      --target-msssim <0-1>    直接设置 MS-SSIM 目标
      --target-mix <0-1>       直接设置加权混合评分目标

      --crf <整数>             手动指定固定 CRF (1-50，同时禁用搜索)
      --crf <最小值>:<最大值>  设置 CRF 搜索范围 (例如 10:50，自动启用搜索)

像素格式:
  -c, --chroma <采样>          色度采样: 420, 422, 444, auto (默认: auto)
  -b, --bit-depth <位数>       输出位深: 8 或 10

其他编码选项:
  -l, --lossless               无损模式 (实验性，可能有兼容性问题)
  -t, --output-template <模板> 输出文件名模板 (默认: covers-{index}.avif)
  -r, --recursive              递归处理子目录

      --serial-encode          极限压缩模式：强制单线程，关闭所有并行
      --prior-search           启用概率分布先验引导搜索（更快）
      --max-resolution <像素>   预缩放：编码前等比缩放至长边不超过该值
      --output-full-res         最终输出保留原始分辨率（仅搜索和指标使用缩放图）
      --proxy                  启用保守代理搜索（需 --prior-search）

      --timeout-encode <分钟>  单次最终编码超时 (默认自动计算)
      --timeout-search <分钟>  搜索阶段全局超时 (默认 60)
      --timeout-safe <分钟>    安全模式全扫描超时 (默认 180)
      --timeout-safe-encode <分钟> 安全模式单次编码超时 (默认 10)
      --timeout-search-encode <分钟> 搜索过程中临时编码超时 (默认 10)
      --timeout-ssim <分钟>    SSIM 计算超时 (默认 5)

通用选项:
  -v, --verbose                详细输出
  -q, --quiet                  安静模式，仅输出错误
  -D, --dry-run                仅打印配置，不实际编码
  -y, --overwrite              覆盖已存在的输出文件
  -n, --no-clobber             跳过已存在的输出文件
  -V, --version                显示版本信息
  -h, --help                   显示此帮助信息

示例:
  # 基础用法
  AvifEncoder -i ./图片 -o ./输出

  # 最佳预设 + 目标 VMAF 95
  AvifEncoder --preset best --target-vmaf 95

  # 使用 420 色度、8bit、固定 CRF 30、不搜索
  AvifEncoder -c 420 -b 8 --crf 30 --no-search

  # 自定义搜索范围与超时
  AvifEncoder --crf 10:45 --target-ssim 0.98 --timeout-search 120
```

## 图形界面（GUI）

启动 `AvifEncoder.Gui.exe` 后将显示窗口界面，包含：

- 输入/输出目录选择
- 预设模式、编码器、质量目标等参数下拉框
- 高级选项：搜索、色度采样、位深、递归、极限压缩等
- 实时日志输出（RichTextBox）
- 进度条显示
- 开始/停止按钮

GUI 会自动调用与 CLI 相同的流水线引擎，并实时报告进度和结果。

## 预设说明

| 预设     | 说明                                                         |
|----------|--------------------------------------------------------------|
| `fast`    | 快速模式：CRF 38, SSIM 0.91, 4:2:0, 8bit, 不搜索  (默认)      |
| `balanced`| 平衡模式：CRF 36, SSIM 0.97, 4:2:0, 8bit, 不搜索             |
| `best`    | 最佳模式：CRF 34, SSIM 0.97, 4:4:4, 8bit, 启用搜索            |
| `extreme` | 极致模式：CRF 35, SSIM 0.99, 4:4:4, 10bit, 启用搜索            |

所有预设均可通过命令行或 GUI 覆盖个别参数。


## 注意事项

- **FFmpeg 依赖**：请确保使用的 FFmpeg 包含 AV1 编码器和 `libvmaf` 滤镜。部分发行版可能未集成，可自行编译或下载静态构建。
- **硬件编码器限制**：硬件编码器通常仅支持 4:2:0 色度采样，且不支持 Alpha 通道和部分高级参数。
- **无损模式**：已知在某些编码器组合下可能失败，建议优先使用 `--crf 0` 实现近无损压缩。
- **长路径**：Windows 下自动启用 `\\?\` 长路径支持，确保深层次目录结构正常处理。
- **临时文件清理**：程序在结束时自动清理 `_scaled`、`_enc_cache`、`avif_metrics_tmp` 等临时目录。即使意外退出也会通过 `finally` 块或 `Dispose` 清理。
- **大规模批量处理**：建议根据 CPU 核心数和内存调整 `-j` 值，避免资源耗尽。


---

**反馈与贡献**：欢迎提交 Issue 或 Pull Request。
