using System;
using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUl.选项窗口
{
    public partial class FormHelp : Form
    {
        public FormHelp()
        {
            InitializeComponent();
            LoadHelpText();
        }

        private void LoadHelpText()
        {
            if (txtHelp != null)
                txtHelp.Text = GetHelpText();
        }

        /// <summary>
        /// 返回完整的使用帮助文本（与命令行版本保持一致）
        /// </summary>
        private static string GetHelpText()
        {
            return @"
===== 命令行帮助文本 =====
AVIF 编码器 —— Linux 风格CLI命令行工具

用法:
  AvifEncoder --input <目录> --output <目录> [选项]
  AvifEncoder -i <目录> -o <目录> [选项]

支持的输入格式:
    """".jpg"""", """".jpeg"""", """".png"""", """".webp"""",
    """".bmp"""", """".tif"""", """".tiff"""", """".gif"""",
    """".jp2"""", """".j2k"""", """".jpx""""

主要选项:
  -i, --input <目录>           输入目录 (默认: input)
  -o, --output <目录>          输出目录 (默认: Avifoutput)
  -p, --preset <预设>          预设模式: fast, balanced, best, extreme (默认: extreme)
  -e, --encoder <名称>         指定 AV1 编码器 (默认: libaom-av1)
  -j, --jobs <数量>            并行任务数 (默认: 根据 CPU 自动计算)

质量控制:
  -s, --search                 启用 CRF 搜索 (默认启用)
      --no-search              禁用 CRF 搜索
      --metric <模式>           质量评价模式: vmaf, ssim, psnr, msssim, mix, XPSNR, ssimu2, butter3, gmsd (默认 vmaf)
                               设置目标分数自动切换模式
      --target-vmaf <0-100>    直接设置 VMAF 目标
      --target-xpsnr <dB>      直接设置 XPSNR 加权综合分目标（默认 W‑XPSNR，配合 --metric xpsnr_y/u/v 可选择通道）
      --target-ssim <0-1>      直接设置 SSIM 目标
      --target-psnr <dB>       直接设置 PSNR-Y 目标 (典型 30-50)
      --target-msssim <0-1>    直接设置 MS-SSIM 目标
      --target-ssimu2 <值>     直接设置 SSIMULACRA2 目标（越大越好，通常取 0~100）
      --target-butter3 <值>    直接设置 Butteraugli 3‑norm 目标（越小越好，通常取 0~10）
      --target-gmsd <值>       直接设置 GMSD 目标（越小越好，通常取 0~1）
      --target-mix <0-1>       直接设置多指标加权混合评分目标

      --crf <整数>             手动指定固定 CRF (1-50，同时禁用搜索)
      --crf <最小值>:<最大值>  设置 CRF 搜索范围 (例如 10:50，自动启用搜索)

像素格式:
  -c, --chroma <采样>          色度采样: 420, 422, 444, auto (默认: auto)
  -b, --bit-depth <位数>       输出位深: 8 或 10

其他编码选项:
  -l, --lossless               无损模式 (有bug，不建议使用)
  -t, --output-template <模板> 输出文件名模板 (默认: covers-{index}.avif)
  -r, --recursive              递归处理子目录

      --serial-encode          极限压缩模式：强制单线程，关闭所有并行（tile/row-mt/内部线程）
                               仅保留 AV1 规范必须的瓦片分割（宽图自动分片）
                               以追求更高压缩率（编码速度会明显变慢）

      --search-cpu-used <0-13> 搜索阶段编码器速度（覆盖预设，默认使用预设值）
                               数值越高编码越快，评估精度下降。不同编码器含义：
                               libaom -cpu-used 0-8 (0最慢最高质)，
                               libsvtav1 -preset 0-13 (0最慢)，
                               librav1e --speed 0-10 (0最慢)
                               最终编码仍使用预设或自定义速度

      --final-cpu-used <0-13>  最终编码阶段编码器速度（覆盖预设，默认使用预设值）
                               数值含义同 --search-cpu-used，但仅影响最终输出文件的编码。
                               如果不指定，最终编码将使用预设的高质量速度（通常较慢）。

      --prior-search           启用概率分布先验引导搜索（中位数+哨兵，通常更快）
                               不启用的情况下默认使用标准二分搜索

      --max-resolution <像素>   预缩放：编码前将图片等比缩放，使长边不超过该值。
                               设为 0 则禁用预缩放，完全按原始分辨率编码（默认 0）。
                               开启后，搜索和质量评估也使用缩放后的图片。
                               若希望搜索用小图加速，但最终保留原图尺寸，需要加上 --output-full-res。

      --proxy                  启用保守代理搜索（需配合 --prior-search），快速评估中位数附近点来缩小区间
      --output-full-res        最终输出保留原始分辨率 (搜索和指标使用缩放后图像)

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
  -y, --overwrite              覆盖已存在的输出文件（默认自动重命名）
  -n, --no-clobber             已存在的文件，直接跳过
  -V, --version                显示版本信息
  -h, --help                   显示此帮助信息

示例:
  AvifEncoder -i ./图片 -o ./输出
  AvifEncoder --preset best --target-vmaf 95
  AvifEncoder -c 420 -b 8 --crf 30 --no-search
  AvifEncoder --crf 10:45 --target-ssim 0.98 --timeout-search 120



 ===== GUI 控件与命令行参数一一对应说明 =====
========== GUI 控件对照表 ========== 
输入/输出目录   -> 文本框 txtInput / txtOutput 
预设模式         -> 下拉框 cmbPreset（fast/balanced/best/extreme/自定义） 
编码器           -> 下拉框 cmbEncoder 
并行任务数       -> 数字框 numJobs（0=自动） 
搜索开关         -> 复选框 chkSearch；CRF 范围/固定值 -> 单选按钮 + numCrfFix / numCrfMin / numCrfMax 
色度采样         -> 下拉框 cmbChroma (auto/420/422/444) 
输出位深         -> 下拉框 cmbBitDepth (auto/8/10) 
质量目标/度量    -> 下拉框 cmbQualityMode + 数字框 numQualityValue 
搜索度量模式     -> 下拉框 cmbMetric 
输出模板         -> 文本框 txtTemplate 
递归子目录       -> 复选框 chkRecursive 
极限压缩         -> 复选框 chkSerialEncode 
先验搜索         -> 复选框 chkPriorSearch 
代理搜索         -> 复选框 chkProxy 
搜索速度         -> 数字框 numSearchCpuUsed（对应 --search-cpu-used） 
最终编码速度     -> 数字框 numFinalCpuUsed（对应 --final-cpu-used） 
预缩放           -> 数字框 numMaxRes + 复选框 chkOutputFullRes 
文件冲突策略     -> 下拉框 cmbConflict 
=================================== 
";
        }
    }
}