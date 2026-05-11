module;

#include <algorithm>
#include <cmath>
#include <cwctype>
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

// 命令行只负责生成这个配置对象；后面的流水线不会再回头解析 argv。
struct AppConfig {
  std::filesystem::path input_dir{L"input"};
  std::filesystem::path output_dir{L"Avifoutput"};
  std::filesystem::path magick_path{};
  std::wstring output_template{L"{name}.avif"};
  std::vector<std::wstring> magick_defines{};
  Preset preset{Preset::best};
  int quality{90};
  std::optional<int> magick_speed{};
  int max_jobs{static_cast<int>(std::max(1u, std::thread::hardware_concurrency()))};
  int max_resolution{0};
  int encode_timeout_minutes{30};
  bool skip_existing{true};
  bool strip_metadata{false};
  bool magick_path_overridden{false};
};

struct ParseResult {
  bool should_exit{false};
  int exit_code{0};
  AppConfig config{};
};

namespace config_detail {

std::wstring lower_copy(std::wstring_view text) {
  std::wstring out{text};
  std::ranges::transform(out, out.begin(),
                         [](wchar_t ch) { return std::towlower(ch); });
  return out;
}

std::string narrow_ascii(std::wstring_view text) {
  std::string out;
  out.reserve(text.size());
  for (const wchar_t ch : text) {
    out.push_back(ch <= 0x7f ? static_cast<char>(ch) : '?');
  }
  return out;
}

template <class Number>
std::optional<Number> scan_number(std::wstring_view text) {
  auto normalized = lower_copy(text);
  if (!normalized.empty() && normalized.front() == L'q') {
    normalized.erase(normalized.begin());
  }

  const auto narrow = narrow_ascii(normalized);
  const auto parsed = scn::scan_value<Number>(std::string_view{narrow});
  if (!parsed) {
    return std::nullopt;
  }
  return parsed->value();
}

std::expected<int, std::string> parse_quality(std::wstring_view text) {
  const auto value = scan_number<double>(text);
  if (!value) {
    return std::unexpected{"质量参数必须是数字，例如 90 或 q90。"};
  }

  const double normalized =
      (*value > 0.0 && *value <= 1.0) ? (*value * 100.0) : *value;
  const int quality = static_cast<int>(std::lround(normalized));
  if (quality < 1 || quality > 100) {
    return std::unexpected{"质量范围必须在 1 到 100 之间。"};
  }
  return quality;
}

std::expected<int, std::string> parse_int_range(std::wstring_view text,
                                                int min_value,
                                                int max_value,
                                                std::string_view name) {
  const auto value = scan_number<int>(text);
  if (!value) {
    return std::unexpected{std::format("{} 必须是整数。", name)};
  }
  if (*value < min_value || *value > max_value) {
    return std::unexpected{
        std::format("{} 范围必须在 {} 到 {} 之间。", name, min_value, max_value)};
  }
  return *value;
}

std::optional<Preset> parse_preset(std::wstring_view value) {
  const auto lower = lower_copy(value);
  if (lower == L"fast") {
    return Preset::fast;
  }
  if (lower == L"balanced") {
    return Preset::balanced;
  }
  if (lower == L"best") {
    return Preset::best;
  }
  if (lower == L"extreme") {
    return Preset::extreme;
  }
  return std::nullopt;
}

void apply_preset(AppConfig& cfg, Preset preset) {
  cfg.preset = preset;
  switch (preset) {
    case Preset::fast:
      cfg.quality = 75;
      cfg.encode_timeout_minutes = 10;
      break;
    case Preset::balanced:
      cfg.quality = 85;
      cfg.encode_timeout_minutes = 20;
      break;
    case Preset::best:
      cfg.quality = 90;
      cfg.encode_timeout_minutes = 30;
      break;
    case Preset::extreme:
      cfg.quality = 95;
      cfg.encode_timeout_minutes = 60;
      break;
  }
}

}  // namespace config_detail

void print_help() {
  constexpr std::string_view help = R"(AVIF Console C++23
==================

默认后端：ImageMagick magick.exe
默认质量：q90

用法:
  AVIFConsoleCpp.exe [选项]

常用选项:
  -i, --input <目录>          输入目录，默认 input
  -o, --output <目录>         输出目录，默认 Avifoutput
  -q, --quality <1-100>       ImageMagick 质量，默认 90。也接受 q90 或 0.9
  -p, --preset <名称>         fast / balanced / best / extreme，默认 best
  -m, --max-jobs <数量>       并发数量，默认 CPU 线程数
  -t, --template <模板>       输出命名，默认 {name}.avif
  --max-resolution <像素>     限制最长边；0 表示不缩放，默认 0
  --speed <0-8>              可选：传给 ImageMagick heic:speed；默认使用 Magick 自身默认值
  --define <key=value>        额外传给 magick 的 -define，可重复
  --backend magick            后端占位参数；当前仅支持 magick
  --magick <路径>             指定 magick.exe；默认优先使用 vendor/imagemagick
  --timeout-encode <分钟>     单张图片编码超时，默认 30
  --strip                    去除元数据
  --overwrite                覆盖已有输出
  --help                     显示帮助

模板变量:
  {index}  图片序号，从 1 开始
  {name}   原文件名，不含扩展名
  {ext}    原扩展名，不含点

示例:
  AVIFConsoleCpp.exe -i "D:\图片" -o Avifoutput -q q90 --speed 0
  AVIFConsoleCpp.exe -i input --max-resolution 2560 --strip
)";
  std::println("{}", help);
}

std::expected<ParseResult, std::string> parse_arguments(
    const std::vector<std::wstring>& args) {
  AppConfig cfg;

  auto require_value = [&](std::size_t& index,
                           std::wstring_view option)
      -> std::expected<std::wstring, std::string> {
    if (index + 1 >= args.size()) {
      return std::unexpected{
        std::format("{} 需要一个参数。", config_detail::narrow_ascii(option))};
    }
    ++index;
    return args[index];
  };

  for (std::size_t i = 0; i < args.size(); ++i) {
    const auto lower = config_detail::lower_copy(args[i]);

    if (lower == L"-h" || lower == L"--help") {
      print_help();
      return ParseResult{.should_exit = true, .exit_code = 0, .config = cfg};
    }

    if (lower == L"-i" || lower == L"--input") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      cfg.input_dir = *value;
      continue;
    }

    if (lower == L"-o" || lower == L"--output") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      cfg.output_dir = *value;
      continue;
    }

    if (lower == L"--magick") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      cfg.magick_path = *value;
      cfg.magick_path_overridden = true;
      continue;
    }

    if (lower == L"--backend") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      const auto backend = config_detail::lower_copy(*value);
      if (backend != L"magick" && backend != L"imagemagick") {
        return std::unexpected{"当前版本仅支持 magick / imagemagick 后端。"};
      }
      continue;
    }

    if (lower == L"-t" || lower == L"--template") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      cfg.output_template = *value;
      continue;
    }

    if (lower == L"-p" || lower == L"--preset") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      const auto preset = config_detail::parse_preset(*value);
      if (!preset) {
        return std::unexpected{
            "预设必须是 fast、balanced、best 或 extreme。"};
      }
      config_detail::apply_preset(cfg, *preset);
      continue;
    }

    if (lower == L"-q" || lower == L"--quality") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      const auto quality = config_detail::parse_quality(*value);
      if (!quality) {
        return std::unexpected{quality.error()};
      }
      cfg.quality = *quality;
      continue;
    }

    if (lower == L"-m" || lower == L"--max-jobs") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      const auto jobs = config_detail::parse_int_range(*value, 1, 128, "并发数量");
      if (!jobs) {
        return std::unexpected{jobs.error()};
      }
      cfg.max_jobs = *jobs;
      continue;
    }

    if (lower == L"--speed") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      const auto speed = config_detail::parse_int_range(*value, 0, 8, "speed");
      if (!speed) {
        return std::unexpected{speed.error()};
      }
      cfg.magick_speed = *speed;
      continue;
    }

    if (lower == L"--max-resolution") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      const auto max_resolution =
          config_detail::parse_int_range(*value, 0, 100000, "max-resolution");
      if (!max_resolution) {
        return std::unexpected{max_resolution.error()};
      }
      cfg.max_resolution = *max_resolution;
      continue;
    }

    if (lower == L"--timeout-encode") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      const auto timeout =
          config_detail::parse_int_range(*value, 1, 24 * 60, "timeout-encode");
      if (!timeout) {
        return std::unexpected{timeout.error()};
      }
      cfg.encode_timeout_minutes = *timeout;
      continue;
    }

    if (lower == L"--define") {
      const auto value = require_value(i, args[i]);
      if (!value) {
        return std::unexpected{value.error()};
      }
      cfg.magick_defines.push_back(*value);
      continue;
    }

    if (lower == L"--strip") {
      cfg.strip_metadata = true;
      continue;
    }

    if (lower == L"--keep-metadata") {
      cfg.strip_metadata = false;
      continue;
    }

    if (lower == L"--overwrite") {
      cfg.skip_existing = false;
      continue;
    }

    if (lower == L"--skip-existing") {
      cfg.skip_existing = true;
      continue;
    }

    return std::unexpected{
        std::format("未知参数: {}", config_detail::narrow_ascii(args[i]))};
  }

  return ParseResult{.should_exit = false, .exit_code = 0, .config = cfg};
}

}  // namespace avif
