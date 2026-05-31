# AVIF 编码器

批量将 JPG/PNG/WebP 等图片转为 AVIF 格式，自动搜索最优编码参数。

---

## 功能

- **双界面**：LakeUI 桌面 GUI + 命令行 CLI
- **三编码器**：libaom-av1 / libsvtav1 / librav1e
- **质量搜索**：二分查找满足目标质量的最优 CRF
- **10 项指标**：VMAF / PSNR / SSIM / MS-SSIM / Mix / XPSNR / SSIMULACRA2 / Butteraugli / GMSD
- **遍历模式**：按 CRF 范围批量编码，生成 RD 曲线数据
- **配置保存**：JSON 持久化所有编码参数
- **自动更新**：GitHub Release 版本检查

---

## 环境要求

- .NET 10.0 Runtime
- ffmpeg + ffprobe（PATH 中或程序目录）
- 至少一个 AV1 编码器（libaom-av1 / libsvtav1 / librav1e）
- ssimulacra2.exe / butteraugli_main.exe（可选，启用对应指标）

---

## 使用

### 桌面 GUI

```bash
./AvifEncoder.GuiLakeUI.exe
```

1. 选择输入/输出目录，或拖拽文件夹到文本框
2. 选择编码器、预设、目标质量
3. 点击「开始」

### 命令行

```bash
# 基础编码
./AvifEncoder -i ./images -o ./output

# 最佳预设 + 目标 VMAF 95
./AvifEncoder --preset best --target-vmaf 95

# 遍历模式
./AvifEncoder -i ./images -o ./output --sweep --crf 20:40
```

#### 常用选项

| 参数 | 说明 |
|------|------|
| `-i` / `--input` | 输入目录 |
| `-o` / `--output` | 输出目录 |
| `-p` / `--preset` | fast / balanced / best / extreme |
| `-e` / `--encoder` | libaom-av1 / libsvtav1 / librav1e |
| `-j` / `--jobs` | 并行任务数 |
| `-s` / `--search` | 启用 CRF 搜索 |
| `--target-vmaf` | VMAF 目标 (0-100) |
| `--target-ssim` | SSIM 目标 (0-1) |
| `--target-psnr` | PSNR 目标 dB (30-50) |
| `--crf N` | 固定 CRF |
| `--crf MIN:MAX` | CRF 搜索范围 |
| `--sweep` | 遍历模式 |
| `-c` / `--chroma` | 色度采样 (420/422/444/auto) |
| `-b` / `--bit-depth` | 位深 (8/10/auto) |
| `-r` / `--recursive` | 递归子目录 |
| `-h` / `--help` | 完整帮助 |

---

## 预设

| 预设 | CRF | 目标 | 搜索 |
|------|-----|------|------|
| fast | 38 | SSIM 0.91 | 关 |
| balanced | 36 | SSIM 0.97 | 开 |
| best | 34 | SSIM 0.97 | 开 |
| extreme | 35 | SSIM 0.99 | 开 |

---

## 本地运行

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
dotnet publish AvifEncoder.GuiLakeUI -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:SelfContained=true -o ./publish
```

版本号在 `Directory.Build.props` 中修改。

---

## 项目结构

```
AvifEncoder.Core/        核心引擎
AvifEncoder.GuiLakeUI/   桌面 GUI（LakeUI 框架）
图片avif压缩控制台/       CLI 命令行
AvifEncoder.Gui/         旧版 GUI（备用）
AvifEncoder.Core.Tests/  单元测试
```
