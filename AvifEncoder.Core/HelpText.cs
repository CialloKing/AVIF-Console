namespace AvifEncoder
{
    public static class HelpText
    {
        public const string CliHelp = @"
AVIF 编码器 —— Linux 风格CLI命令行工具

用法:
  AvifEncoder --input <目录> --output <目录> [选项]
  AvifEncoder -i <目录> -o <目录> [选项]

支持的输入格式（默认）:
    "".jpg"", "".jpeg"", "".png"", "".webp"", "".gif""
    如需其他格式请使用 --extensions 指定

输入过滤:
  -x, --extensions <.ext,.ext> 限制输入图片格式，逗号分隔 (例: "".jpg,.png"")
                               默认 5 种，可选: bmp tif tiff gif jp2 j2k jpx avif
                              GIF 动图自动检测帧数，编码为动画 AVIF

主要选项:
  -i, --input <目录>           输入目录 (默认: input)
  -o, --output <目录>          输出目录 (默认: Avifoutput)
  -p, --preset <预设>          预设模式: fast, balanced, best, extreme (默认: balanced)
  -e, --encoder <名称>         指定 AV1 编码器 (默认: libaom-av1)
      --enc-params <参数>      编码器私有参数，直接传递到 ffmpeg 命令行（含前缀）
                               空字符串则清空所有私有参数。示例:
      --denoise <0-15>         编码降噪，0=关闭 (默认: 0)
                               libaom: 映射 arnr-strength(0-6)，arnr-max-frames 自动推导
                               libsvtav1: 映射 film-grain(0-15)
      --arnr-strength <0-6>   libaom ARNr 降噪强度，arnr-max-frames 自动推导
      --arnr-max-frames <0-15> libaom ARNr 参考帧数，arnr-strength 默认 4
                               2-3=温和, 4-6=中等, 7-10=激进, 11-15=极限
                               仅 libaom-av1 有效，其他编码器忽略
      --rgb-mode <模式>        RGB 直通模式: off(关闭, 默认) / auto(自动) / gbrp / gbrap / gbrp16le
                               off: 强制使用 YUV 色彩空间（默认）
                               auto: 源文件为 RGB 时自动直通，YUV 时走常规流程
                               仅 libaom-av1 有效
      --enc-params <参数>      编码器私有参数，直接传递到 ffmpeg 命令行（含前缀）
                               空字符串则清空所有私有参数。示例:
                                 libaom:    --enc-params ""-aom-params sharpness=2:enable-cdef=0""
                                 libsvtav1: --enc-params ""-svtav1-params tune=3:film-grain=8:enable-qm=0""
                                 librav1e:  --enc-params ""-rav1e-params quantizer=100""
                                 NVENC:     --enc-params ""-tune hq -preset p7""
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


      --crf <整数>            手动指定固定 CRF (0-63，同时禁用搜索)
      --crf <最小值>:<最大值>  设置 CRF 搜索范围 (例如 10:50，自动启用搜索)

像素格式:
  -c, --chroma <采样>          色度采样: 420, 422, 444, auto (默认: auto)
  -b, --bit-depth <位数>       输出位深: 8, 10, auto (默认: auto)
                              当设为 auto 时由程序根据源文件自动选择

其他编码选项:
  -l, --lossless               无损模式（编解码后逐像素验证）
  -t, --output-template <模板>  输出文件名模板 (默认: covers-{index}.avif)
                               可用占位符及示例(假设原图 photo.png, encoder=libaom-av1, crf=30, speed=4):
                                 {name}      → photo
                                 {ext}       → .png
                                 {dir}       → photos (源文件所在目录名)
                                 {index}     → 01 (默认2位)
                                 {index:000} → 001 (3位补零)
                                 {encoder}   → libaom-av1
                                 {crf}       → 30
                                 {speed}     → 4
                                 {pixfmt}    → yuv444p
                                 {lossless}  → lossless 或 lossy
                                 {bitdepth}  → 8 / 10 / 12
                                 {date}      → 2022-02-22
                                 {time}      → 22-22-22
                               推荐模板及输出示例:
                                 {name}.avif                          → photo.avif
                                 {name}_{encoder}_crf{crf}.avif       → photo_libaom-av1_crf30.avif
                                 {name}_{crf}_{pixfmt}.avif           → photo_30_yuv444p.avif
                                 {date}/{name}_{crf}.avif             → 2022-02-22/photo_30.avif
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

      --sweep                 遍历模式：对每张图片在 MinCRF～MaxCRF 范围内逐个编码并保存所有结果。
                              文件名自动附加 _CRF数字，CSV 包含完整统计数据
                              使用此选项可用于生成 RD 曲线数据，或分析不同 CRF 设置下的质量/文件大小关系

      --recompute-metrics      强制重新计算所有质量指标（忽略缓存）

      --resume                 断点续传：跳过已完成的文件，从中断处继续

      --timeout-encode <分钟>  单次最终编码超时 (默认自动计算)
      --timeout-search <分钟>  搜索阶段全局超时 (默认 60)
      --timeout-safe <分钟>    安全模式全扫描超时 (默认 180)
      --timeout-safe-encode <分钟> 安全模式单次编码超时 (默认 10)
      --timeout-search-encode <分钟> 搜索过程中临时编码超时 (默认 10)
      --timeout-ssim <分钟>    SSIM 计算超时 (默认 5)

通用选项:
  -v, --verbose                详细输出
  -q, --quiet                  安静模式，仅输出错误
  -D, --dry-run                仅打印配置，不实际编码，用于验证命令行是否正确，或查看程序将如何执行
  -y, --overwrite              覆盖已存在的输出文件（默认行为是自动添加 _1 等后缀）
  -n, --no-clobber             已存在的文件，直接跳过
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

  # 自定义编码器私有参数
  AvifEncoder --encoder libaom-av1 --enc-params ""-aom-params sharpness=2:enable-cdef=0""
";

        public const string GuiControlTable = @"
 ===== GUI 控件与命令行参数一一对应说明 =====
========== GUI 控件对照表 ========== 
输入/输出目录   -> 文本框 txtInput / txtOutput 
预设模式         -> 下拉框 cmbPreset（fast/balanced/best/extreme/自定义） 
编码器           -> 下拉框 cmbEncoder 
并行任务数       -> 数字框 numJobs（0=自动） 
搜索开关         -> 复选框 chkSearch；CRF 范围/固定值 -> 单选按钮 + numCrfFix / numCrfMin / numCrfMax 
色度采样         -> 下拉框 cmbChroma (auto/420/422/444) 
输出位深         -> 下拉框 cmbBitDepth (auto/8/10/12) 
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
编码器高级参数   -> 文本框 txtEncoderParams（选项页）
ffmpeg 命令预览  -> 文本框 txtParamsPreview（选项页，只读）
=================================== 
";

        /// <summary>
        /// GUI 使用说明页面正文。
        /// </summary>
        public const string GuiGuide = @"
═══════════════════════════════════════
  AVIF 编码器 —— 使用指南
═══════════════════════════════════════

【整体流程】

  选择目录 → 设置参数 → 点击开始 → 自动完成

  程序会依次执行：
    1) 扫描输入目录中的所有图片文件
    2) 对每张图片探测像素格式、色彩元数据、Alpha 通道
    3) 根据目标质量启动 CRF 搜索（二分查找最优 CRF）
    4) 用最优 CRF 编码输出 AVIF 文件
    5) 计算质量指标（SSIM/VMAF/PSNR/XPSNR 等）
    6) 导出 CSV 统计报告

═══════════════════════════════════════

【编码器选择】

  ┌────────────┬────────┬──────────┬──────────────────────────────┐
  │ 编码器      │ 压缩率  │ 速度     │ 适用场景                      │
  ├────────────┼────────┼──────────┼──────────────────────────────┤
  │ libaom-av1 │ ★★★★★  │ ★☆☆☆☆   │ 归档、发行、追求最小体积       │
  │ libsvtav1  │ ★★★★☆  │ ★★★★☆   │ 批量处理、服务器、日常使用     │
  │ librav1e   │ ★★★☆☆  │ ★★★☆☆   │ 动漫、插画、心理视觉优先       │
  │ av1_nvenc  │ ★★☆☆☆  │ ★★★★★   │ 快速预览、实时转码             │
  │ av1_qsv    │ ★★☆☆☆  │ ★★★★☆   │ 笔记本低功耗场景               │
  │ av1_amf    │ ★★☆☆☆  │ ★★★★☆   │ AMD 显卡用户快速转码           │
  └────────────┴────────┴──────────┴──────────────────────────────┘

  libaom-av1（推荐默认）：
    官方参考编码器，画质与压缩率的黄金标准。
    独有功能：AOM 高级参数调优（aq-mode、deltaq-mode 等）、
    still-picture 单帧模式、row-mt 行级并行、完整 tile 分片。
    cpu-used 范围 0（最慢/最高质量）~ 8（最快）。

  libsvtav1：
    Intel 与 Netflix 联合开发的 SVT-AV1 编码器。
    多核并行效率极高，适合批量处理大量图片。
    独有功能：film-grain 噪点合成、qm 量化矩阵调优。
    preset 范围 0（最慢/最高质量）~ 13（最快），界面已自动反转。

  librav1e：
    Xiph.Org 维护的 Rust 编码器，心理视觉调优出色。
    对动漫线条、UI 截图、插画等场景有良好表现。
    speed 范围 0（最慢）~ 10（最快）。

  硬件编码器 (NVENC / QSV / AMF / VAAPI)：
    依赖 GPU 硬件加速，编码速度极快，但压缩率不如软件编码器。
    不支持无损模式、tile 分片、AOM 高级参数。
    画质可控参数较少，适合快速预览而非最终交付。

═══════════════════════════════════════

【预设模式】

  预设决定了 CRF 起点、目标质量和是否启用搜索：

  fast      — CRF 38, SSIM 0.91, 不搜索。速度优先。
  balanced  — CRF 36, SSIM 0.97, 启用搜索。质量与速度平衡。
  best      — CRF 34, SSIM 0.97, 启用搜索。高质量。
  extreme   — CRF 35, SSIM 0.99, 启用搜索。极致质量。
  自定义     — 手动修改任意参数后自动切换为此模式。

═══════════════════════════════════════

【CRF 搜索工作原理】

  CRF (Constant Rate Factor) 是 AV1 的质量参数：
  • 值越小 → 质量越高，文件越大
  • 值越大 → 质量越低，文件越小

  启用搜索后，程序会自动找到满足目标质量的『最大 CRF』
  ——即在保证质量达标的前提下，让文件尽可能小。

  搜索流程：
    1) 根据 VMAF 先验表估算中位数 CRF（基于真实图片统计数据）
    2) 可选用极快参数做代理评估，进一步缩小搜索区间
    3) 在确定区间内执行标准二分查找
    4) 每次迭代：临时编码 → 计算质量分数 →
       达标则尝试更大 CRF，否则降低 CRF
    5) 若搜索失败，启用安全模式兜底（yuv420p + 单 tile + 全色域）

  搜索失败的处理：
    • 当 MinCRF=0 时，CRF=0 已是质量上限，跳过安全扫描直接编码
    • 当 MinCRF>0 时，启动安全模式逐 CRF 全扫描直到找到可行解
    • 若仍然失败，回退使用预设的 BaseCRF 进行编码

  性能提示：
    • 启用先验搜索可减少 30~50% 的搜索次数
    • 启用代理搜索可进一步缩小搜索区间
    • 提高搜索速度（cpu-used/preset）可加速搜索，但精度略降
    • 小图片的搜索开销通常可忽略，大图片（>4K）搜索耗时显著

═══════════════════════════════════════

【质量目标与搜索度量】

  质量目标（cmbQualityMode + numQualityValue）设定编码后期望达到的质量水平。
  搜索度量（cmbMetric）决定用哪个指标来评价编码结果。

  ── 指标详解 ──

  VMAF (Video Multi-Method Assessment Fusion)
    Netflix 开发的感知质量模型，基于机器学习融合多种基础指标。
    范围 0~100，越高越好。典型目标：95（极高）/ 93（高）/ 90（良好）。
    计算需调用 libvmaf，耗时较长但准确性最高。
    推荐作为默认指标，适合绝大多数场景。

  PSNR-Y (Peak Signal-to-Noise Ratio, Y 通道)
    最传统的客观质量指标，逐像素比较亮度差异。
    范围 0 ~ +∞ dB，越高越好。完全一致 = +∞（无损）。
    典型目标：45 dB（极高）/ 40 dB（高）/ 35 dB（可接受）。
    计算最快，但与主观感知相关性弱，不推荐作为唯一指标。

  SSIM (Structural SIMilarity)
    模拟人眼对结构信息的敏感度，考虑亮度、对比度和结构三个维度。
    范围 0~1，越高越好。完全一致 = 1.0。
    典型目标：0.98（极高）/ 0.95（高）/ 0.90（良好）。
    经典指标，计算快速，适合快速评估。

  MS-SSIM (Multi-Scale SSIM)
    SSIM 的改进版，在多个分辨率尺度上评估。
    范围 0~1，越高越好。典型目标：0.995（极高）/ 0.99（高）。
    比单尺度 SSIM 更接近主观感知，计算开销略高。

  XPSNR (Weighted eXtended PSNR)
    专为 HDR 和高位深内容设计的感知 PSNR 变体。
    范围 -∞ ~ +∞ dB，越高越好。权重 Y:U:V = 6:1:1。
    典型目标：50 dB（高）/ 45 dB（良好）。
    可选子通道：xpsnr_y / xpsnr_u / xpsnr_v / xpsnr_w（加权）。
    需要 ffmpeg 4.4+ 内置支持。

  SSIMULACRA 2
    目前最准确的图片感知质量评估工具之一。
    范围 -∞ ~ +∞，越高越好。典型值：90+（极高）/ 80+（高）/ 70+（良好）。
    极低质量编码可能出现负值。
    需要外部工具 ssimulacra2.exe（需提前安装到 PATH）。
    可检测传统指标难以发现的模糊、振铃、色块等伪影。

  Butteraugli 3-norm
    Google 开发的感知差异度量，模拟人眼对伪影的敏感度。
    范围 0 ~ +∞，越小越好。完全一致 = 0。
    典型目标：1.0（极高）/ 2.0（高）/ 3.0（可接受）。
    需要外部工具 butteraugli_main.exe（需提前安装到 PATH）。
    对 JPEG/AVIF 压缩伪影高度敏感，极差质量可达数十甚至数百。

  GMSD (Gradient Magnitude Similarity Deviation)
    基于图像梯度的感知质量指标，对边缘和纹理失真敏感。
    范围 0~1（可能略超 1），越小越好。完全一致 = 0。
    典型目标：0.05（极高）/ 0.10（高）。
    内置实现，无需外部工具，基于 ffmpeg 解码灰度数据计算。
    适合快速评估边缘保留质量。

  MixScore (综合加权评分)
    融合多种基础指标的综合分数，范围 0~1，越高越好。
    无 XPSNR 时：VMAF 80% + MS-SSIM 10% + SSIM 5% + PSNR-Y 5%
    有 XPSNR 时：VMAF 50% + XPSNR 32% + MS-SSIM 8% + SSIM 5% + PSNR-Y 5%
    推荐用于需要综合考量的自动决策场景。

  ── 使用建议 ──

  追求感知质量：  SSIMULACRA 2 或 VMAF
  追求保真度：    PSNR-Y 或 SSIM
  HDR 内容：      XPSNR
  快速预览：      SSIM 或 PSNR-Y
  综合评估：      MixScore
  边缘/纹理质量： GMSD

  推荐组合：SSIMULACRA 2 + Butteraugli 对图片压缩的感知准确性最高。
  但需额外安装外部工具。若无法安装，VMAF 是最佳替代。

═══════════════════════════════════════

【遍历模式】

  勾选「遍历模式」后，程序对每张图片在 MinCRF~MaxCRF 范围内
  逐个 CRF 值生成独立的 AVIF 文件。

  输出文件：
    文件名自动附加 _CRF{值}，如 image_CRF20.avif、image_CRF21.avif …
    所有结果写入同一 CSV，每行对应一个 CRF 值的完整质量数据。

  用途：
    • 生成 RD（码率-失真）曲线，可视化质量与文件大小的关系
    • 分析不同 CRF 下各指标（VMAF/SSIM/PSNR 等）的变化趋势
    • 找到特定质量目标对应的最优 CRF
    • 对比不同编码器在相同 CRF 下的表现

  注意事项：
    • 遍历模式强制关闭 CRF 搜索
    • CRF 范围过大时文件数量多、耗时长
    • 建议先用窄范围（如 25~35）试运行，再扩大范围
    • 遍历模式仍然继承其他编码参数（色度、位深、速度等）

═══════════════════════════════════════

【搜索速度与最终速度】

  这两个参数控制传递给编码器的速度等级，不同编码器含义不同：

  libaom：   cpu-used 0（最慢/最高质）~ 8（最快）
  libsvtav1：preset 0（最慢/最高质）~ 13（最快），界面已自动反转
  librav1e： speed 0（最慢/最高质）~ 10（最快）
  NVENC：    preset p7（最慢/最高质）~ p1（最快），界面已自动反转

  搜索速度（numSearchCpuUsed）：
    影响 CRF 搜索阶段每次临时编码的快慢。
    • 值越高搜索越快，但质量评估精度下降
    • 可能导致搜索到非最优 CRF（精度损失 < 2%）
    • 推荐值：libaom=4~6, libsvtav1=4, librav1e=6
    • 不指定时使用预设值

  最终速度（numFinalCpuUsed）：
    影响最终输出文件的编码速度。
    • 通常设为较低值（~2）以获得最佳压缩率
    • 对速度要求高时，可设为搜索速度的一半
    • 不指定时使用预设值（通常为 2）

═══════════════════════════════════════

【色度采样与位深】

  色度采样（chroma）：
    420 — 色度分辨率减半，肉眼几乎不可察觉，文件最小。
    422 — 色度水平减半，适合广播级素材。
    444 — 色度无损，适合文字、UI 截图、动漫线条。
    auto — 自动跟随源文件格式。

  位深（bit-depth）：
    8 bit — 标准位深，适用于绝大多数 SDR 图片。
    10 bit — 高位深，减少色带效应，适合 HDR 或渐变丰富的图片。
    auto — 自动跟随源文件格式。

═══════════════════════════════════════

【极限压缩模式】

  勾选「单线程极限压缩」后：
    • 强制单线程编码（-threads 1）
    • 关闭 row-mt 行级并行
    • 使用最小合法 tile 分片

  效果：压缩率略微提升，但编码速度显著下降。
  适合最终交付前的最后一轮精编码。

═══════════════════════════════════════

【编码器高级参数（选项页）】

  在「选项」标签页中，可以自定义编码器私有参数：

  「自定义 ffmpeg 命令行高级参数」文本框：
    • 根据当前选择的编码器，自动填入默认私有参数
    • 切换编码器时自动覆盖为对应默认值
    • 可手动编辑，点击右侧按钮恢复默认
    • 对应 CLI 选项 --enc-params

  「实际使用 ffmpeg 完整命令」文本框：
    • 实时显示拼接后的完整 ffmpeg 命令行预览
    • 自动反映当前所有编码选项（色度、位深、CRF 等）
    • auto 选项会标注「← 色度/位深由源文件决定」
    • CRF 搜索模式显示为「-crf 搜索: 20~40」
    • 点击右侧按钮可复制完整命令到剪贴板

  提示：修改编码页任何选项后，预览会实时更新。

═══════════════════════════════════════════════════════════════════════

【编码降噪（选项页）】

  在「选项」页中可用数字框设置降噪强度（0-15）。

  降噪是提升 AVIF 压缩率的关键手段之一：
    • 编码前去除图像中的自然噪点，编码器可更高效压缩
    • 解码端根据元数据合成噪点还原观感（Film Grain 合成）
    • 噪点丰富的图片（手机拍摄、低光、扫描件）收益最大

  强度建议：
    0     关闭 — 动漫、UI 截图、文字、纯色图
    2-3   温和 — 低 ISO 自然照片，几乎无可见差异
    4-6   中等 — 手机拍摄、ISO 800-1600，体积 -15%~25%
    7-10  激进 — 高噪点老照片，体积 -30%~40%
    11-15 极限 — 极小体积优先，接受柔和/涂抹感

  编码器映射：
    libaom-av1 → arnr-strength (钳制 0-6)
    libsvtav1  → film-grain (直接使用 0-15)
    librav1e / 硬件编码器 → 不支持，自动忽略

  提示：降噪参数嵌入到 -aom-params / -svtav1-params 末尾。
  若同时使用自定义参数（--enc-params）指定了相同 key，
  自定义参数优先（ffmpeg last-wins 覆盖）。

═══════════════════════════════════════

【RGB 直通（选项页）】

  在「选项」页中可用下拉框选择 RGB 色彩空间模式。

  常规 AVIF 编码会将 RGB 转为 YUV，解码时再转回 RGB。
  两次色彩空间转换会导致色度损失，尤其影响：
    • 红蓝边界出现伪影
    • 细线条（UI 截图、图标）变模糊
    • 纯色区域产生轻微色偏

  RGB 直通跳过 YUV 转换，直接在 RGB 空间编码：
    • libaom-av1 编码器原生支持（其他编码器自动禁用）
    • 8 位 RGB（gbrp）：适合 UI 截图、图表、文字密集图片
    • 8 位 RGBA（gbrap）：保留 Alpha 透明通道，适合 PNG 图标
    • 16 位 RGB（gbrp16le）：高位深，适合摄影导出、HDR

  选项：
    关闭（默认）— 强制使用 YUV 色彩空间
    自动 — 源文件为 RGB 时自动直通，YUV 时走常规流程
    gbrp — 强制 8 位 RGB 直通
    gbrap — 强制 8 位 RGBA 直通（含 Alpha）
    gbrp16le — 强制 16 位 RGB 直通

  对应 CLI 选项：--rgb-mode auto/off/gbrp/gbrap/gbrp16le

═══════════════════════════════════════

【先验搜索与代理搜索】

  先验搜索（prior-search）：
    基于 400 张真实图片的统计数据，预先估算最可能的最优
    CRF 中位数，直接划定搜索区间，减少无效尝试。

  代理搜索（proxy）：
    在正式二分搜索前，用极快参数（yuv420p + 高速）快速
    验证 3 个 CRF 点，进一步缩小搜索范围。

  两者配合可显著加速搜索，尤其对大分辨率图片。
  不启用时，使用标准二分搜索在全范围内查找。

═══════════════════════════════════════

【预缩放】

  勾选预缩放后，编码前先将图片等比缩放至指定长边分辨率。
  • 搜索和质量评估均使用缩放后的图片（速度更快）
  • 最终输出可独立选择是否保留原图分辨率

  例如：设置预缩放为 1920，勾选「保持原图分辨率」
  → 搜索用小图加速，但最终输出原始大小的 AVIF。

═══════════════════════════════════════

【控件交叉影响关系】

  选择编码器后：
    → 自动调整搜索/最终速度上限（libaom=8, svt=13, rav1e=10）
    → 硬件编码器不支持无损、still-picture、AOM 高级参数

  勾选无损模式后：
    → 自动关闭 CRF 搜索（无损不需要）
    → 自动关闭遍历模式
    → 强制使用 yuv444p 像素格式

  选择预设后：
    → 自动填充 CRF 值、目标质量、搜索开关
    → 修改任意参数后自动切换为「自定义」

  勾选遍历模式后：
    → 自动关闭 CRF 搜索
    → 强制使用 CRF 范围模式
    → 文件冲突策略在遍历模式下仍可设置

═══════════════════════════════════════

【配置文件的保存与加载】

  「保存配置到文件」将当前所有设置（字体、窗口、编码参数）
  导出为 JSON 文件，可分享给他人或备份。

  「从文件加载配置」导入之前保存的 JSON 配置。

  程序启动时自动加载 exe 目录下或工作目录下的
  app_settings.json（不自动创建，需手动保存一次）。

═══════════════════════════════════════

═══════════════════════════════════════

【断点续传】

  当编码任务因意外中断（断电、崩溃、手动停止），
  重新打开程序时会自动检测到未完成的任务。

  检测到中断后：
    • 编码页顶部显示「检测到未完成任务，是否继续？」
    • 点击「恢复任务」从中断处继续，已完成文件不重复编码
    • 点击「放弃任务」清除中断状态，重新开始全新编码
    • 恢复后编码参数自动从快照中还原（无需重新设置）

  工作原理：
    程序在 .session/journal.ndjson 中记录每个文件的处理状态。
    每 500 个事件生成一份快照（snapshot.json）。
    恢复时加载快照 → 增量回放日志 → 跳过已完成文件。

  注意事项：
    • 输出目录变更后断点续传可能失效
    • 中途修改编码参数可能导致结果不一致
    • 正常完成后 .session 目录会自动清理

═══════════════════════════════════════

【无损模式】

  勾选「无损模式」后，编码和解码后的像素完全一致。

  特性：
    • 强制使用 yuv444p 像素格式（色度无损）
    • 自动关闭 CRF 搜索（无损不需要）
    • 自动关闭遍历模式
    • 自动关闭预缩放（避免改变像素值）

  验证机制：
    编码完成后逐像素比对原图与解码结果。
    若发现任何差异，输出文件被隔离到 _failed_verification/ 目录，
    并生成详细诊断报告（CSV + JSON），记录差异位置和通道分布。

  适用场景：
    • 医学影像、档案数字化等不可接受任何损失的场景
    • 中间工作文件（后续还需进一步编辑）
    • 注意：无损 AVIF 文件通常比有损大 2~5 倍

═══════════════════════════════════════

【并行任务数】

  同时运行的编码任务数量。

  auto（设为 0 时）：
    • 软件编码器：√CPU核数（如 16 核 → 4 并发）
    • 硬件编码器：CPU核数 × 2（如 8 核 → 16 并发）
    • 硬件编码器对 CPU 占用低，可安全超订

  手动设置：
    • 建议不超过 CPU 核数
    • 运行时可通过「更新并发」按钮动态调整
    • 减少并发可降低内存和磁盘 I/O 压力
    • 过高的并发可能导致编码失败（内存不足或超时）

═══════════════════════════════════════

【超时设置（选项页）】

  防止单张图片编码卡死导致整个任务停止。

  单次编码超时（0=自动）：
    根据图片分辨率自动计算（约 1920×1080 → 10 分钟）。
    设为 0 时使用自动计算值。设正值强制指定分钟数。

  搜索全局超时（默认 60 分钟）：
    整个 CRF 搜索阶段的总时间上限。
    超时后使用已找到的最佳 CRF 进行最终编码。

  安全模式超时（默认 180 分钟）：
    兜底安全扫描的总时间上限。
    仅在普通搜索失败时启用。

  SSIM 计算超时（默认 5 分钟）：
    单次质量评估计算的超时。
    大分辨率图片或慢速存储可能需要调高。

═══════════════════════════════════════

【图片后缀名（选项页）】

  指定要处理的图片文件格式。

  默认值：.jpg,.jpeg,.png,.webp,.gif
  自定义：用英文逗号分隔，如 .jpg,.png,.bmp,.tiff

  注意事项：
    • 输入输出目录相同时自动排除 .avif 文件（避免循环编码）；目录不同时 .avif 可作为输入格式正常使用（避免循环编码）
    • 扩展名不区分大小写
    • 点击右侧按钮可恢复默认

═══════════════════════════════════════

【文件冲突策略】

  当输出目录中已存在同名文件时：
    自动重命名 — 追加 _1、_2 等后缀（默认）
    覆盖已存在   — 直接覆盖，不询问
    跳过已存在   — 不编码该文件

═══════════════════════════════════════

【输出文件名模板】

  默认模板：covers-{index}.avif → covers-01.avif

  ── 基础占位符（假设原图 photo.png 位于 photos/ 目录）──
    {name}        → photo             源文件主名
    {ext}         → .png              源扩展名
    {dir}         → photos            源文件所在目录名
    {index}       → 01                默认2位数字序号
    {index:000}   → 001               自定义宽度补零
    {index:0000}  → 0001              4位补零

  ── 编码参数占位符（假设 libaom-av1, crf=30, cpu-used=4）──
    {encoder}     → libaom-av1       编码器名
    {crf}         → 30               CRF值
    {speed}       → 4                cpu-used值
    {pixfmt}      → yuv444p          像素格式
    {lossless}    → lossless         无损/有损标识
    {bitdepth}    → 10               位深

  ── 时间占位符 ──
    {date}        → 2022-02-22
    {time}        → 22-22-22
    {datetime}    → 2022-02-22_22-22-22

  ── 模板示例及输出 ──
    {name}.avif                          → photo.avif
    {name}_{encoder}_crf{crf}.avif       → photo_libaom-av1_crf30.avif
    {name}_{crf}_{pixfmt}.avif           → photo_30_yuv444p.avif
    {date}/{name}_{crf}.avif             → 2022-02-22/photo_30.avif
    {name}_{encoder}_crf{crf}_s{speed}_{pixfmt}.avif
      → photo_libaom-av1_crf30_s4_yuv444p10le.avif

═══════════════════════════════════════════════════════════════════════════

【输出文件说明】

  编码完成后输出目录中会生成以下内容：

  *.avif                           AVIF 编码结果文件
  avif_stats.csv                   完整统计报告，包含所有指标
  _failed_verification/             无损验证失败文件隔离目录
    └─ failed_verification.csv     失败摘要（CSV）
    └─ *.report.json                逐文件详细诊断报告
  log/                             运行日志目录
    └─ run_YYYY-MM-DD.log          每日运行日志
    └─ error.log                   错误日志
    └─ crf_search.log              CRF 搜索详细记录
  _enc_cache/                      编码缓存目录（可安全删除）
  .session/                        断点续传数据（正常完成自动清理）

  CSV 报告包含以下栏目：
    文件名、原始大小(字节)、输出大小(字节)、压缩率、源文件宽、源文件长、动图、帧数、FPS、CRF、
    SSIM、VMAF、PSNR-Y、MS-SSIM、MixScore、
    XPSNR-Y/U/V/W、SSIMULACRA2、Butteraugli_Raw/3norm、
    GMSD、编码耗时、搜索耗时、重试次数、像素格式、
    安全模式、AOM 参数、缓存复用、状态、失败原因
    （动图的质量指标为所有帧的平均值）

═══════════════════════════════════════

【检查更新】

  点击「检查更新」按钮，程序向 GitHub Releases 页面查询
  是否有新版本。若有新版本：
    1) 弹窗展示版本号和文件大小
    2) 用户选择「下载更新」→ 自动下载新 exe
    3) 下载完成后提示重启
    4) 程序自动替换旧版并启动新版

  检查结果仅保存在内存中，不写入磁盘。
";
    }
}
