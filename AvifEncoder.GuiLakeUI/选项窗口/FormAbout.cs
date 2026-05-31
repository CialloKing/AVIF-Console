using System;
using System.Reflection;
using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUI.选项窗口
{
    public partial class FormAbout : Form
    {
        public FormAbout()
        {
            InitializeComponent();
            LoadAboutText();
            txtAbout.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.LinkText) { UseShellExecute = true }); } catch { }
            };
        }

        private void LoadAboutText()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";
            string about = $@"AVIF 编码器  v{version}

===== 功能特性 =====
- 批量将 JPG/PNG/WebP 转为 AVIF 格式
- 自动搜索最优 CRF 编码参数
- 支持自定义选择编码器 (ffmpeg + libaom-av1 / libsvtav1 / librav1e)
- 10 项质量指标 (VMAF/SSIM/PSNR/XPSNR/SSIMULACRA2/Butteraugli/GMSD 等)
- 断点续传、无损验证、遍历模式
- 遍历模式生成 RD 曲线数据

===== 本项目的起源 =====
最开始只是想搓一个简单的powershell脚本来批量转图，后来添加了更多功能，就放弃了脚本，改用C#重写，结果就越做越大了……
因为是从脚本发展而来，原本也没想着做UI界面，其他用户觉得UI界面会更方便一些，就用原生WinForms做了个UI界面。
后来又使用了LakeUI框架来更新更现代化的UI，就是目前的这个版本。


===== 友情链接 =====
这是本项目仓库链接，如果你对这个项目感兴趣，欢迎点个star，或者提交issue和pull request
手动更新失败时也可以在这里下载最新版本
GitHub:            https://github.com/CialloKing/AVIF-Console
Releases:          https://github.com/CialloKing/AVIF-Console/releases

这是本项目用到的计算SSIMULACRA2和Butteraugli的libjxl仓库地址
在发布页找到jxl-x64-windows-static并下载，就能找到需要的ssimulacra2.exe和butteraugli_main.exe
libjxl:            https://github.com/libjxl/libjxl

这是目前使用的LakeUI框架的仓库地址，UI界面就是基于这个框架开发的
LakeUI:            https://github.com/Lake1059/LakeUI

这是LakeUI框架的作者Lake1059的FFmpegFreeUI的仓库地址，功能非常强大，如果你需要一个专业的FFmpeg交互界面，可以去体验这个项目
FFmpegFreeUI是在 Windows 上的 FFmpeg 的专业交互外壳
FFmpegFreeUI:      https://github.com/Lake1059/FFmpegFreeUI



下面这些是群友的类似项目，这些项目和本项目拥有相同的起源但是又有不同的目标
本项目只支持输出AVIF格式的图片，如果需要其他格式比如jxl，webp等等，可以使用下面的项目
(共同点就是本项目和下面的项目都大量使用AI开发)

AWJimage这个项目是一位群友对本项目的早期版本使用C++改写的，后来又添加了更多功能，是一个全能的图片格式转换器，支持多种图片格式
AWJimage 是一个 Windows C++23 / Slint 批量图片转换工具
AWJimage:          https://github.com/Dominic485649/AWJimage

这个项目是另一位群友根据自己的想法搓出来的
一个基于 Avalonia UI 的跨平台图片批量转换工具，底层调用系统已安装的 ffmpegffprobe，将复杂的命令行参数变为直观的图形界面
ffmpegPictureUI:   https://github.com/luoye-cpu/ffmpegPictureUI




===== 技术栈 =====
语言: C# (.NET 10.0)
UI:   LakeUI / WinForms
编码: ffmpeg + libaom-av1 / libsvtav1 / librav1e


";
            txtAbout.Text = about;
        }
    }
}
