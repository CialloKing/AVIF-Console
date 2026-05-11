module;

#include <algorithm>
#include <cmath>
#include <cwctype>
#include <exception>
#include <expected>
#include <filesystem>
#include <format>
#include <optional>
#include <print>
#include <scn/scan.h>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

export module avif.config;

export namespace avif {

enum class Preset { fast, balanced, best, extreme };
enum class Chroma { auto_source, yuv420, yuv422, yuv444 };

// 所有命令行参数最终都会落到这个配置对象，后续流水线只读它。
struct AppConfig {
  std::filesystem::path input_dir{L"input"};
  std::filesystem::path output_dir{L"Avifoutput"};
  Preset preset{Preset::extreme};
  Chroma chroma{Chroma::auto_source};

  int base_crf{35};
  int min_crf{1};
  int max_crf{38};
  double target_ssim{0.99};
  int final_cpu_used{0};
  int search_cpu_used{0};
  int max_jobs{2};
  int bit_depth{10};
  int max_resolution{2560};
  int encode_timeout_minutes{0};

  bool use_crf_search{true};
  bool lossless{false};
  bool auto_source{true};
  bool user_set_bit_depth{false};
  bool output_full_res{false};

  std::string encoder{"libaom-av1"};
  std::string metric{"ssim"};
  std::wstring output_template{L"covers-{index}.avif"};
  std::string aom_params{
      "aq-mode=3:deltaq-mode=0:enable-chroma-deltaq=1:sharpness=0:"
      "enable-qm=1:enable-restoration=1:enable-cdef=1:"
      "enable-global-motion=1:enable-warped-motion=1:"
      "enable-obmc=1:enable-ref-frame-mvs=1:enable-tx64=1:"
      "enable-dist-wtd-comp=1"};
};

struct ParseResult {
  AppConfig config{};
  bool should_exit{false};
  int exit_code{0};
};

int default_thread_count() {
  const auto cores = std::max(1u, std::thread::hardware_concurrency());
  return std::max(2, static_cast<int>(std::sqrt(static_cast<double>(cores))));
}

std::wstring lower_copy(std::wstring text) {
  std::ranges::transform(text, text.begin(),
                         [](wchar_t ch) { return std::towlower(ch); });
  return text;
}

std::string narrow_ascii(std::wstring_view text) {
  std::string out;
  out.reserve(text.size());
  for (const auto ch : text) {
    out.push_back(ch >= 0 && ch <= 0x7f ? static_cast<char>(ch) : '?');
  }
  return out;
}

std::wstring trim_quotes(std::wstring text) {
  while (!text.empty() && (text.front() == L'"' || text.front() == L'\'')) {
    text.erase(text.begin());
  }
  while (!text.empty() && (text.back() == L'"' || text.back() == L'\'')) {
    text.pop_back();
  }
  return text;
}

template <class Number>
std::optional<Number> scan_number(std::wstring_view text) {
  auto narrow = narrow_ascii(text);
  auto parsed = scn::scan_value<Number>(std::string_view{narrow});
  if (!parsed) {
    return std::nullopt;
  }
  return parsed->value();
}

std::wstring require_value(const std::vector<std::wstring>& args,
                           std::size_t& index,
                           std::wstring_view option) {
  if (index + 1 >= args.size()) {
    throw std::runtime_error(std::format("{} 需要一个值", narrow_ascii(option)));
  }
  return args[++index];
}

Preset parse_preset(std::wstring_view text) {
  const auto value = lower_copy(std::wstring{text});
  if (value == L"fast") {
    return Preset::fast;
  }
  if (value == L"balanced") {
    return Preset::balanced;
  }
  if (value == L"best") {
    return Preset::best;
  }
  if (value == L"extreme") {
    return Preset::extreme;
  }
  throw std::runtime_error("预设必须为 fast / balanced / best / extreme");
}

AppConfig make_preset(Preset preset) {
  AppConfig cfg;
  cfg.preset = preset;
  cfg.max_jobs = default_thread_count();

  switch (preset) {
    case Preset::fast:
      cfg.base_crf = 38;
      cfg.target_ssim = 0.91;
      cfg.final_cpu_used = 2;
      cfg.search_cpu_used = 4;
      cfg.use_crf_search = false;
      cfg.chroma = Chroma::yuv420;
      cfg.auto_source = false;
      cfg.bit_depth = 8;
      break;
    case Preset::balanced:
      cfg.base_crf = 36;
      cfg.target_ssim = 0.97;
      cfg.final_cpu_used = 2;
      cfg.search_cpu_used = 2;
      cfg.use_crf_search = false;
      cfg.chroma = Chroma::yuv420;
      cfg.auto_source = false;
      cfg.bit_depth = 8;
      break;
    case Preset::best:
      cfg.base_crf = 34;
      cfg.target_ssim = 0.97;
      cfg.final_cpu_used = 0;
      cfg.search_cpu_used = 2;
      cfg.use_crf_search = true;
      cfg.chroma = Chroma::yuv444;
      cfg.auto_source = false;
      cfg.bit_depth = 8;
      break;
    case Preset::extreme:
      cfg.base_crf = 35;
      cfg.target_ssim = 0.99;
      cfg.final_cpu_used = 0;
      cfg.search_cpu_used = 0;
      cfg.use_crf_search = true;
      cfg.chroma = Chroma::yuv444;
      cfg.auto_source = false;
      cfg.bit_depth = 10;
      break;
  }
  return cfg;
}

void print_help() {
  constexpr std::string_view help = R"(AVIF Console C++23
==================

用法:
  AVIFConsoleCpp.exe -i <输入目录> -o <输出目录> [选项]

基本参数:
  -i <dir>                 输入文件夹，默认 input
  -o <dir>                 输出文件夹，默认 Avifoutput
  -h, --help               显示帮助

预设:
  -p fast|balanced|best|extreme
                            默认 extreme。预设可被后续参数覆盖。

质量:
  -s                       启用 CRF 二分搜索，搜索指标为 SSIM
  -n                       禁用搜索，直接使用预设或 -r 指定的 CRF
  -q <0..1>                搜索目标 SSIM
  -r <crf>                 手动 CRF，搜索禁用时生效
  -r <min:max>             搜索范围，需配合 -s
  -l                       无损模式

像素格式:
  -a                       源自适应
  -c                       强制 4:2:0
  -g                       强制 4:2:2
  -f                       强制 4:4:4
  -d 8|10                  指定位深

批处理:
  -t <n>                   并行处理线程数，默认 sqrt(逻辑核心数)
  -m <模板>                输出名模板，支持 {name}/{index} 或 {{name}}/{{index}}
  --encoder <name>         ffmpeg AV1 编码器，默认 libaom-av1
  --max-resolution <px>    输出缩放长边上限，默认 2560；0 表示禁用
  --output-full-res        不对最终输出做预缩放
  --timeout-encode <min>   单次 ffmpeg 编码超时，默认按分辨率估算

示例:
  .\AVIFConsoleCpp.exe -i input -o Avifoutput -p best -s -q 0.98
  .\AVIFConsoleCpp.exe -i pics -o out -n -r 32 -c -d 8
  .\AVIFConsoleCpp.exe -i pngs -o avifs -l -m {name}.avif

说明:
  C++ 版保留批量扫描、预设、命名、日志、CSV、ffmpeg/ffprobe 调用和
  SSIM 搜索核心流程，去掉 C# 版里较重的缓存/多指标/多级安全扫描代码。
  数值参数使用 scnlib 解析，scnlib 来自 vcpkg。
)";
  std::print("{}", help);
}

ParseResult parse_arguments_or_throw(const std::vector<std::wstring>& args) {
  struct Pending {
    Preset preset{Preset::extreme};
    bool force_search{false};
    bool force_no_search{false};
    bool force_lossless{false};
    bool auto_source_set{false};
    bool has_crf{false};
    bool has_quality{false};
    bool has_threads{false};
    bool has_bit_depth{false};
    bool has_min_crf{false};
    bool has_max_crf{false};
    bool has_timeout{false};
    int crf{};
    int threads{};
    int bit_depth{};
    int min_crf{};
    int max_crf{};
    int timeout{};
    double quality{};
    Chroma chroma{Chroma::auto_source};
    AppConfig paths_and_strings{};
  } pending;

  for (std::size_t i = 0; i < args.size(); ++i) {
    const auto arg = args[i];
    const auto lower = lower_copy(arg);

    if (lower == L"-h" || lower == L"--help") {
      print_help();
      return {.should_exit = true, .exit_code = 0};
    }

    if (lower == L"--output-full-res") {
      pending.paths_and_strings.output_full_res = true;
      continue;
    }

    if (lower == L"--encoder") {
      pending.paths_and_strings.encoder =
          narrow_ascii(require_value(args, i, L"--encoder"));
      continue;
    }
    if (lower.starts_with(L"--encoder=")) {
      pending.paths_and_strings.encoder = narrow_ascii(arg.substr(10));
      continue;
    }

    if (lower == L"--metric") {
      const auto metric = lower_copy(require_value(args, i, L"--metric"));
      if (metric != L"ssim") {
        throw std::runtime_error("C++ 简化版当前只支持 --metric ssim");
      }
      pending.paths_and_strings.metric = "ssim";
      continue;
    }
    if (lower.starts_with(L"--metric=")) {
      const auto metric = lower_copy(arg.substr(9));
      if (metric != L"ssim") {
        throw std::runtime_error("C++ 简化版当前只支持 --metric ssim");
      }
      pending.paths_and_strings.metric = "ssim";
      continue;
    }

    if (lower == L"--max-resolution") {
      const auto value = require_value(args, i, L"--max-resolution");
      const auto parsed = scan_number<int>(value);
      if (!parsed || *parsed < 0) {
        throw std::runtime_error("--max-resolution 必须是非负整数");
      }
      pending.paths_and_strings.max_resolution = *parsed;
      continue;
    }
    if (lower.starts_with(L"--max-resolution=")) {
      const auto parsed = scan_number<int>(arg.substr(17));
      if (!parsed || *parsed < 0) {
        throw std::runtime_error("--max-resolution 必须是非负整数");
      }
      pending.paths_and_strings.max_resolution = *parsed;
      continue;
    }

    if (lower == L"--timeout-encode") {
      const auto value = require_value(args, i, L"--timeout-encode");
      const auto parsed = scan_number<int>(value);
      if (!parsed || *parsed <= 0) {
        throw std::runtime_error("--timeout-encode 必须是正整数");
      }
      pending.has_timeout = true;
      pending.timeout = *parsed;
      continue;
    }
    if (lower.starts_with(L"--timeout-encode=")) {
      const auto parsed = scan_number<int>(arg.substr(17));
      if (!parsed || *parsed <= 0) {
        throw std::runtime_error("--timeout-encode 必须是正整数");
      }
      pending.has_timeout = true;
      pending.timeout = *parsed;
      continue;
    }

    if (!arg.starts_with(L"-") || arg.size() == 1) {
      throw std::runtime_error(std::format("未知参数: {}", narrow_ascii(arg)));
    }

    const auto flags = arg.substr(1);
    if (flags == L"p") {
      pending.preset = parse_preset(require_value(args, i, L"-p"));
    } else if (flags == L"i") {
      pending.paths_and_strings.input_dir =
          std::filesystem::path{require_value(args, i, L"-i")};
    } else if (flags == L"o") {
      pending.paths_and_strings.output_dir =
          std::filesystem::path{require_value(args, i, L"-o")};
    } else if (flags == L"m") {
      pending.paths_and_strings.output_template =
          trim_quotes(require_value(args, i, L"-m"));
    } else if (flags == L"t") {
      const auto value = scan_number<int>(require_value(args, i, L"-t"));
      if (!value || *value <= 0) {
        throw std::runtime_error("-t 必须是正整数");
      }
      pending.has_threads = true;
      pending.threads = *value;
    } else if (flags == L"d") {
      const auto value = scan_number<int>(require_value(args, i, L"-d"));
      if (!value || (*value != 8 && *value != 10)) {
        throw std::runtime_error("-d 只能是 8 或 10");
      }
      pending.has_bit_depth = true;
      pending.bit_depth = *value;
      pending.auto_source_set = true;
      pending.chroma = Chroma::auto_source;
    } else if (flags == L"q") {
      const auto value = scan_number<double>(require_value(args, i, L"-q"));
      if (!value || *value < 0.0 || *value > 1.0) {
        throw std::runtime_error("-q 必须是 0..1 之间的 SSIM 数值");
      }
      pending.has_quality = true;
      pending.quality = *value;
    } else if (flags == L"r") {
      const auto value = require_value(args, i, L"-r");
      if (const auto pos = value.find(L':'); pos != std::wstring::npos) {
        const auto low = scan_number<int>(value.substr(0, pos));
        const auto high = scan_number<int>(value.substr(pos + 1));
        if (!low || !high || *low < 0 || *high > 63 || *low >= *high) {
          throw std::runtime_error("-r 搜索范围格式应为 min:max，且 0<=min<max<=63");
        }
        pending.has_min_crf = true;
        pending.has_max_crf = true;
        pending.min_crf = *low;
        pending.max_crf = *high;
      } else {
        const auto crf = scan_number<int>(value);
        if (!crf || *crf < 0 || *crf > 63) {
          throw std::runtime_error("-r CRF 必须是 0..63 的整数");
        }
        pending.has_crf = true;
        pending.crf = *crf;
      }
    } else {
      for (const auto flag : flags) {
        switch (flag) {
          case L's':
            pending.force_search = true;
            pending.force_no_search = false;
            break;
          case L'n':
            pending.force_no_search = true;
            pending.force_search = false;
            break;
          case L'a':
            pending.auto_source_set = true;
            pending.chroma = Chroma::auto_source;
            break;
          case L'c':
            pending.auto_source_set = true;
            pending.chroma = Chroma::yuv420;
            break;
          case L'g':
            pending.auto_source_set = true;
            pending.chroma = Chroma::yuv422;
            break;
          case L'f':
            pending.auto_source_set = true;
            pending.chroma = Chroma::yuv444;
            break;
          case L'l':
            pending.force_lossless = true;
            break;
          default:
            throw std::runtime_error(
                std::format("未知选项: -{}", static_cast<char>(flag)));
        }
      }
    }
  }

  auto cfg = make_preset(pending.preset);
  cfg.input_dir = pending.paths_and_strings.input_dir;
  cfg.output_dir = pending.paths_and_strings.output_dir;
  cfg.encoder = pending.paths_and_strings.encoder;
  cfg.metric = pending.paths_and_strings.metric;
  cfg.output_template = pending.paths_and_strings.output_template;
  cfg.max_resolution = pending.paths_and_strings.max_resolution;
  cfg.output_full_res = pending.paths_and_strings.output_full_res;

  if (pending.force_lossless) {
    cfg.base_crf = 0;
    cfg.target_ssim = 1.0;
    cfg.final_cpu_used = 0;
    cfg.search_cpu_used = 0;
    cfg.use_crf_search = false;
    cfg.lossless = true;
    cfg.bit_depth = 10;
    cfg.chroma = Chroma::yuv444;
    cfg.auto_source = false;
    cfg.aom_params = "aq-mode=0:deltaq-mode=0:enable-chroma-deltaq=0";
  }

  if (pending.force_search) {
    cfg.use_crf_search = true;
  }
  if (pending.force_no_search) {
    cfg.use_crf_search = false;
  }
  if (pending.has_quality) {
    cfg.target_ssim = pending.quality;
  }
  if (pending.has_threads) {
    cfg.max_jobs = pending.threads;
  }
  if (pending.has_bit_depth) {
    cfg.bit_depth = pending.bit_depth;
    cfg.user_set_bit_depth = true;
  }
  if (pending.auto_source_set) {
    cfg.chroma = pending.chroma;
    cfg.auto_source = pending.chroma == Chroma::auto_source;
  }
  if (pending.has_min_crf) {
    cfg.min_crf = pending.min_crf;
  }
  if (pending.has_max_crf) {
    cfg.max_crf = pending.max_crf;
  }
  if (pending.has_timeout) {
    cfg.encode_timeout_minutes = pending.timeout;
  }
  if (pending.has_crf) {
    if (cfg.use_crf_search && !cfg.lossless) {
      std::println("[WARN] -r <crf> 仅在 -n 禁用搜索时生效，已忽略。");
    } else {
      cfg.base_crf = pending.crf;
    }
  }
  if (cfg.min_crf >= cfg.max_crf) {
    throw std::runtime_error("最小 CRF 必须小于最大 CRF");
  }
  cfg.max_jobs = std::max(1, cfg.max_jobs);
  return {.config = cfg};
}

std::expected<ParseResult, std::string> parse_arguments(
    const std::vector<std::wstring>& args) noexcept {
  try {
    return parse_arguments_or_throw(args);
  } catch (const std::exception& ex) {
    return std::unexpected{std::string{ex.what()}};
  } catch (...) {
    return std::unexpected{std::string{"未知参数解析错误"}};
  }
}

}  // namespace avif
