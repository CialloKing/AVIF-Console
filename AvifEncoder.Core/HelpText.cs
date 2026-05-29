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
    "".jpg"", "".jpeg"", "".png"", "".webp""
    如需其他格式请使用 --extensions 指定

输入过滤:
  -x, --extensions <.ext,.ext> 限制输入图片格式，逗号分隔 (例: "".jpg,.png"")
                               默认 4 种，可选: bmp tif tiff gif jp2 j2k jpx avif

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


      --crf <整数>            手动指定固定 CRF (1-50，同时禁用搜索)
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
                                 {bitdepth}  → 8 或 10
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

  libaom-av1   — 官方参考编码器，压缩率最高，速度最慢。
                  支持 still-picture、row-mt、AOM 高级参数调优。
                  适合追求极致压缩率的场景。

  libsvtav1    — Intel/Netflix 联合开发，多核并行极强。
                  适合批量高速编码、服务器端处理。

  librav1e     — Rust 编写的现代化编码器，简洁快速。
                  适合动漫、插画、UI 截图等。

  硬件编码器    — av1_nvenc/av1_qsv/av1_amf 等，速度最快但
                  压缩率通常不如软件编码器，不支持无损模式。

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
    2) 用极快参数（yuv420p + 高速度）做代理评估，缩小搜索范围
    3) 在缩小的范围内执行标准二分查找
    4) 每次二分迭代：用当前 CRF 编码临时文件 → 计算质量分数
       → 若达标则上移 CRF（尝试更大压缩），否则下移
    5) 若搜索失败，启用安全模式（最保守参数）兜底

═══════════════════════════════════════

【质量目标与搜索度量】

  搜索度量（--metric / cmbMetric）决定了用哪个指标评价质量。
  质量目标（target / numQualityValue + cmbQualityMode）设定期望值。

  ┌──────────────┬──────────┬────────────────────────┐
  │ 指标          │ 方向     │ 典型目标               │
  ├──────────────┼──────────┼────────────────────────┤
  │ VMAF         │ 越大越好 │ 95（极高）             │
  │ PSNR-Y       │ 越大越好 │ 45 dB（极高）          │
  │ SSIM         │ 越大越好 │ 0.98（高）             │
  │ MS-SSIM      │ 越大越好 │ 0.995（极高）           │
  │ XPSNR        │ 越大越好 │ 50 dB（高）            │
  │ SSIMULACRA2  │ 越大越好 │ 85+（极高）            │
  │ Butteraugli  │ 越小越好 │ 1.0（高）              │
  │ GMSD         │ 越小越好 │ 0.05（高）             │
  └──────────────┴──────────┴────────────────────────┘

  推荐：SSIMULACRA2 + Butteraugli 组合对图片压缩感知最准确。

═══════════════════════════════════════

【遍历模式】

  勾选「遍历模式」后，程序会在 MinCRF~MaxCRF 范围内
  逐个 CRF 值生成独立的 AVIF 文件。

  文件名自动附加 _CRF{值}，如 image_CRF25.avif。
  完成后 CSV 中每行对应一个 CRF 值的完整质量数据。

  用途：生成 RD（码率-失真）曲线，分析不同 CRF 下的
  质量与文件大小关系，找到最优平衡点。

═══════════════════════════════════════

【搜索速度与最终速度】

  这两个参数控制 ffmpeg 传递给编码器的速度等级：

  libaom   — cpu-used 0（最慢最高质）~ 8（最快）
  libsvtav1 — preset 13（最慢）~ 0（最快），界面已自动反转
  librav1e  — speed 0（最慢）~ 10（最快）

  搜索速度：影响 CRF 搜索阶段每次临时编码的快慢。
            值越高搜索越快，但质量评估精度下降，
            可能导致搜索到非最优 CRF。

  最终速度：影响最终输出文件的编码速度。
            通常设为较低值以获得最佳压缩率。

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
