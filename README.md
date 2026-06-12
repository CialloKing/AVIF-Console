# AVIF 编码器 V2.1.0


（本项目所有代码均由AI完成，绝无手写代码，本段说明手写）

批量将 JPG / PNG / WebP / GIF 等图片转为 AVIF 格式，自动搜索最优编码参数。

---

## 功能

- **双界面**：LakeUI 桌面 GUI + 命令行 CLI
- **多编码器**：libaom-av1 / libsvtav1 / librav1e + 硬件编码器（NVENC / QSV / AMF / VAAPI）
- **质量搜索**：二分查找满足目标质量的最优 CRF，支持 VMAF / PSNR / SSIM / MS-SSIM / XPSNR / SSIMULACRA2 / Butteraugli / GMSD
- **动图支持**：GIF → AVIF，Alpha 双流、逐帧指标平均、自定义编码模板
- **断点续传**：中断后从上次位置恢复，进度 / CSV / 指标完整接续
- **指标跳过**：`--skip-metrics` 按需跳过耗时计算，节省 10~35s/文件
- **遍历模式**：按 CRF 范围批量编码，导出 RD 曲线数据
- **Toast 通知**：编码完成 / 异常时弹出系统通知，带进度条
- **配置持久化**：JSON 保存所有参数，GUI 启动自动恢复

---

## 环境要求

- .NET 10.0 Runtime（fdd版需要，scd版自带.NET 10.0）
- ffmpeg + ffprobe（PATH 中或程序目录）包含至少一个 AV1 编码器
- ssimulacra2.exe / butteraugli_main.exe（可选，启用对应指标）

---

## 使用

### 桌面 GUI

```bash
./AvifEncoder.GuiLakeUI.exe
```

### 命令行

```bash
# 基础编码
./AvifEncoder -i ./images -o ./output

# 目标 VMAF 95 + 最佳预设
./AvifEncoder --preset best --target-vmaf 95

# 跳过高耗时指标，加速搜索
./AvifEncoder -i ./images --skip-metrics ssimu2,butter3,gmsd,xpsnr

# 动图编码
./AvifEncoder -i ./anime.gif -o ./output

# 断点续传
./AvifEncoder -i ./images --resume

# 遍历模式
./AvifEncoder -i ./images --sweep --crf 20:40
```

#### 常用选项

| 参数 | 说明 |
|------|------|
| `-i` / `--input` | 输入目录或文件 |
| `-o` / `--output` | 输出目录 |
| `-p` / `--preset` | fast / balanced / best / extreme |
| `-e` / `--encoder` | libaom-av1 / libsvtav1 / librav1e |
| `--target-vmaf` | VMAF 目标 (0-100) |
| `--target-ssim` | SSIM 目标 (0-1) |
| `--target-psnr` | PSNR 目标 dB |
| `--target-xpsnr` | XPSNR 目标 dB (40-60) |
| `--target-ssimu2` | SSIMULACRA2 目标 |
| `--target-butter3` | Butteraugli 3-norm 目标 |
| `--target-gmsd` | GMSD 目标 |
| `--skip-metrics` | 跳过指标（逗号分隔）：xpsnr / ssimu2 / butter3 / gmsd / psnr_uncapped / all_advanced |
| `--crf N` | 固定 CRF (0-63) |
| `--crf MIN:MAX` | CRF 搜索范围 |
| `--sweep` | 遍历模式 |
| `--resume` | 断点续传 |
| `-c` / `--chroma` | 色度采样：420 / 422 / 444 / auto |
| `-b` / `--bit-depth` | 位深：8 / 10 / 12 / auto |
| `--rgb-mode` | RGB 直通：off / gbrp / gbrap / gbrp16le |
| `--denoise` | 降噪强度 (0-15) |
| `--enc-params` | 编码器自定义参数 |
| `-r` / `--recursive` | 递归子目录 |
| `-h` / `--help` | 完整帮助 |

---

## 本地开发

```bash
# 编译
dotnet build 图片avif压缩控制台.slnx

# GUI
dotnet run --project AvifEncoder.GuiLakeUI

# CLI
dotnet run --project 图片avif压缩控制台 -- --help

# 测试
dotnet test AvifEncoder.Core.Tests
```

### 发布

```bash
# 免安装版
dotnet publish AvifEncoder.GuiLakeUI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# 轻量版
dotnet publish AvifEncoder.GuiLakeUI -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

版本号在 `Directory.Build.props` 中修改。

---

## 项目结构

```
AvifEncoder.Core/         核心引擎
AvifEncoder.GuiLakeUI/    桌面 GUI（LakeUI 框架）
图片avif压缩控制台/        CLI 命令行
AvifEncoder.Gui/          旧版 GUI（备用）
AvifEncoder.Core.Tests/   单元测试与集成测试（173 个）
```
