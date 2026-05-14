module;

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <MagickWand/MagickWand.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <expected>
#include <filesystem>
#include <format>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <vector>
#include <windows.h>

export module avif.magick_backend;

import avif.config;
import avif.core;

export namespace avif {

struct MagickRuntime {
  fs::path root{};
  bool bundled{false};
};

namespace magick_detail {

std::optional<fs::path> absolute_directory(fs::path path) {
  std::error_code ec;
  path = fs::absolute(std::move(path), ec);
  if (ec) {
    return std::nullopt;
  }
  if (fs::is_regular_file(path, ec) && !ec) {
    path = path.parent_path();
  }
  if (fs::is_directory(path, ec) && !ec) {
    return path;
  }
  return std::nullopt;
}

bool looks_like_magick_runtime(const fs::path& root) {
  std::error_code ec;
  const bool has_wand_dll =
      fs::exists(root / L"CORE_RL_MagickWand_.dll", ec) && !ec;
  const bool has_wand_lib =
      fs::exists(root / L"lib" / L"CORE_RL_MagickWand_.lib", ec) && !ec;
  const bool has_config = fs::exists(root / L"configure.xml", ec) && !ec;
  const bool has_include =
      fs::exists(root / L"include" / L"MagickWand" / L"MagickWand.h", ec) &&
      !ec;
  return has_wand_dll || has_wand_lib || has_config || has_include;
}

std::optional<fs::path> existing_runtime_root(fs::path path) {
  auto root = absolute_directory(std::move(path));
  if (!root || !looks_like_magick_runtime(*root)) {
    return std::nullopt;
  }
  return root;
}

void collect_ancestor_runtime_candidates(std::vector<fs::path>& candidates,
                                         fs::path start) {
  std::error_code ec;
  start = fs::absolute(start, ec);
  if (ec) {
    return;
  }

  for (auto current = start; !current.empty(); current = current.parent_path()) {
    candidates.push_back(current);
    candidates.push_back(current / L"third_party" /
                         L"imagemagick-runtime" / L"x64" / L"Release");
    if (current == current.root_path()) {
      break;
    }
  }
}

std::optional<fs::path> environment_runtime() {
  std::wstring buffer(32768, L'\0');
  const DWORD size =
      GetEnvironmentVariableW(L"AVIF_MAGICK", buffer.data(),
                              static_cast<DWORD>(buffer.size()));
  if (size == 0 || size >= buffer.size()) {
    return std::nullopt;
  }
  buffer.resize(size);
  return existing_runtime_root(buffer);
}

std::vector<fs::path> bundled_candidates() {
  std::vector<fs::path> candidates;
  collect_ancestor_runtime_candidates(candidates, executable_directory());
  collect_ancestor_runtime_candidates(candidates, fs::current_path());
  candidates.push_back(L"D:\\Scoop\\apps\\imagemagick\\current");
  return candidates;
}

void set_env_if_directory(std::wstring_view name, const fs::path& path) {
  std::error_code ec;
  if (fs::is_directory(path, ec) && !ec) {
    const auto value = path.native();
    SetEnvironmentVariableW(std::wstring{name}.c_str(), value.c_str());
  }
}

std::string magick_exception(MagickWand* wand, std::string_view fallback) {
  if (wand == nullptr) {
    return std::string{fallback};
  }

  ExceptionType severity{};
  char* raw = MagickGetException(wand, &severity);
  if (raw == nullptr) {
    return std::string{fallback};
  }

  std::string message{raw};
  MagickRelinquishMemory(raw);
  if (message.empty()) {
    return std::string{fallback};
  }
  return message;
}

struct WandDeleter {
  void operator()(MagickWand* wand) const noexcept {
    if (wand != nullptr) {
      DestroyMagickWand(wand);
    }
  }
};

using WandPtr = std::unique_ptr<MagickWand, WandDeleter>;

void ensure_magick_initialized() {
  static std::once_flag once;
  std::call_once(once, [] { MagickWandGenesis(); });
}

std::expected<void, std::string> check(MagickWand* wand,
                                       MagickBooleanType status,
                                       std::string_view action) {
  if (status != MagickFalse) {
    return {};
  }
  return std::unexpected{std::format("{}: {}", action,
                                     magick_exception(wand, "MagickWand 调用失败"))};
}

std::string define_key(std::string_view define) {
  const auto pos = define.find('=');
  if (pos == std::string_view::npos) {
    return std::string{define};
  }
  return std::string{define.substr(0, pos)};
}

std::string define_value(std::string_view define) {
  const auto pos = define.find('=');
  if (pos == std::string_view::npos) {
    return "true";
  }
  return std::string{define.substr(pos + 1)};
}

}  // namespace magick_detail

void configure_magick_environment(const MagickRuntime& runtime) {
  const auto root = runtime.root.native();
  SetEnvironmentVariableW(L"MAGICK_HOME", root.c_str());
  SetEnvironmentVariableW(L"MAGICK_CONFIGURE_PATH", root.c_str());

  magick_detail::set_env_if_directory(
      L"MAGICK_CODER_MODULE_PATH", runtime.root / L"modules" / L"coders");
  magick_detail::set_env_if_directory(
      L"MAGICK_FILTER_MODULE_PATH", runtime.root / L"modules" / L"filters");
}

std::expected<MagickRuntime, std::string> resolve_magick_runtime(
    const AppConfig& cfg) {
  if (cfg.magick_path_overridden) {
    if (const auto direct =
            magick_detail::existing_runtime_root(cfg.magick_path)) {
      return MagickRuntime{.root = *direct, .bundled = false};
    }
    return std::unexpected{
        std::format("未找到指定的 ImageMagick 运行时目录: {}",
                    path_to_utf8(cfg.magick_path))};
  }

  if (const auto from_env = magick_detail::environment_runtime()) {
    return MagickRuntime{.root = *from_env, .bundled = false};
  }

  for (const auto& candidate : magick_detail::bundled_candidates()) {
    if (const auto bundled = magick_detail::existing_runtime_root(candidate)) {
      const bool is_dev_fallback =
          normalized_lower_path_key(*bundled) ==
          normalized_lower_path_key(L"D:\\Scoop\\apps\\imagemagick\\current");
      return MagickRuntime{.root = *bundled, .bundled = !is_dev_fallback};
    }
  }

  return std::unexpected{
      "未找到 ImageMagick 运行时。请先运行 scripts\\build-magick.ps1，或用 --magick/AVIF_MAGICK 指定目录。"};
}

class MagickBackend {
 public:
  MagickBackend(const AppConfig& cfg,
                const MagickRuntime& runtime,
                FileLogger& logger)
      : cfg_{cfg}, runtime_{runtime}, logger_{logger} {
    (void)runtime_;
    magick_detail::ensure_magick_initialized();
  }

  EncodeResult encode(const ImageFile& image) const {
    const auto start = std::chrono::steady_clock::now();
    const auto output = cfg_.output_dir / output_name_for(cfg_, image);
    EncodeResult result{.index = image.index,
                        .input_path = image.path,
                        .output_path = output,
                        .original_bytes = image.bytes,
                        .output_bytes = 0,
                        .quality = cfg_.quality,
                        .speed = cfg_.magick_speed.value_or(-1),
                        .command = command_description(image, output)};

    try {
      std::error_code ec;
      fs::create_directories(output.parent_path(), ec);
      if (ec) {
        result.message =
            std::format("无法创建输出目录: {}", path_to_utf8(output.parent_path()));
        return finish(result, start);
      }

      if (cfg_.skip_existing && fs::exists(output, ec) &&
          fs::file_size(output, ec) > 0) {
        result.ok = true;
        result.skipped = true;
        result.output_bytes = fs::file_size(output, ec);
        result.message = "已存在，跳过。";
        return finish(result, start);
      }

      auto wand = magick_detail::WandPtr{NewMagickWand()};
      if (!wand) {
        result.message = "无法创建 MagickWand。";
        logger_.error(result.message);
        return finish(result, start);
      }

      if (auto ok = read_and_prepare(*wand, image); !ok) {
        result.message = ok.error();
        logger_.error(result.message);
        return finish(result, start);
      }

      if (auto ok = write_avif(*wand, output); !ok) {
        result.message = ok.error();
        logger_.error(result.message);
        return finish(result, start);
      }

      if (!fs::exists(output, ec) || fs::file_size(output, ec) == 0 || ec) {
        result.message = "MagickWand 已结束，但没有生成有效输出文件。";
        logger_.error(result.message);
        return finish(result, start);
      }

      result.output_bytes = fs::file_size(output, ec);
      result.ok = true;
      result.message = "完成。";
      logger_.info(std::format("encode: {}", result.command));
      return finish(result, start);
    } catch (const std::exception& ex) {
      result.message = std::format("异常: {}", ex.what());
      logger_.error(result.message);
      return finish(result, start);
    } catch (...) {
      result.message = "未知异常。";
      logger_.error(result.message);
      return finish(result, start);
    }
  }

 private:
  static EncodeResult finish(EncodeResult result,
                             std::chrono::steady_clock::time_point start) {
    const auto end = std::chrono::steady_clock::now();
    result.seconds = std::chrono::duration<double>(end - start).count();
    return result;
  }

  std::expected<void, std::string> read_and_prepare(MagickWand& wand,
                                                    const ImageFile& image) const {
    const auto input = path_to_utf8(image.path);
    if (auto ok = magick_detail::check(&wand, MagickReadImage(&wand, input.c_str()),
                                       "读取图片失败");
        !ok) {
      return ok;
    }

    if (auto ok = magick_detail::check(&wand, MagickAutoOrientImage(&wand),
                                       "自动旋转失败");
        !ok) {
      return ok;
    }

    if (auto ok = resize_if_needed(wand); !ok) {
      return ok;
    }

    if (cfg_.strip_metadata) {
      if (auto ok = magick_detail::check(&wand, MagickStripImage(&wand),
                                         "去除元数据失败");
          !ok) {
        return ok;
      }
    }

    return {};
  }

  std::expected<void, std::string> resize_if_needed(MagickWand& wand) const {
    if (cfg_.max_resolution <= 0) {
      return {};
    }

    const auto width = MagickGetImageWidth(&wand);
    const auto height = MagickGetImageHeight(&wand);
    const auto longest = std::max(width, height);
    if (longest <= static_cast<std::size_t>(cfg_.max_resolution)) {
      return {};
    }

    const double scale =
        static_cast<double>(cfg_.max_resolution) / static_cast<double>(longest);
    const auto next_width =
        std::max<std::size_t>(1, static_cast<std::size_t>(std::llround(width * scale)));
    const auto next_height =
        std::max<std::size_t>(1, static_cast<std::size_t>(std::llround(height * scale)));
    return magick_detail::check(&wand,
                                MagickResizeImage(&wand, next_width, next_height,
                                                  LanczosFilter),
                                "缩放图片失败");
  }

  std::expected<void, std::string> write_avif(MagickWand& wand,
                                              const fs::path& output) const {
    if (auto ok = magick_detail::check(
            &wand,
            MagickSetImageCompressionQuality(
                &wand, static_cast<std::size_t>(cfg_.quality)),
            "设置质量失败");
        !ok) {
      return ok;
    }

    if (cfg_.magick_speed) {
      const auto value = std::to_string(*cfg_.magick_speed);
      if (auto ok = magick_detail::check(&wand,
                                         MagickSetOption(&wand, "heic:speed",
                                                         value.c_str()),
                                         "设置 heic:speed 失败");
          !ok) {
        return ok;
      }
    }

    for (const auto& define : cfg_.magick_defines) {
      const auto define_utf8 = utf8_from_wide(define);
      const auto key = magick_detail::define_key(define_utf8);
      const auto value = magick_detail::define_value(define_utf8);
      if (key.empty()) {
        return std::unexpected{"--define 不能为空。"};
      }
      if (auto ok = magick_detail::check(&wand,
                                         MagickSetOption(&wand, key.c_str(),
                                                         value.c_str()),
                                         std::format("设置 define {} 失败", key));
          !ok) {
        return ok;
      }
    }

    if (auto ok = magick_detail::check(&wand, MagickSetImageFormat(&wand, "AVIF"),
                                       "设置 AVIF 格式失败");
        !ok) {
      return ok;
    }

    const auto output_utf8 = path_to_utf8(output);
    return magick_detail::check(&wand,
                                MagickWriteImage(&wand, output_utf8.c_str()),
                                "写入 AVIF 失败");
  }

  std::string command_description(const ImageFile& image,
                                  const fs::path& output) const {
    std::string text =
        std::format("MagickWand: {} -> {} -quality {}",
                    path_to_utf8(image.path), path_to_utf8(output), cfg_.quality);
    if (cfg_.magick_speed) {
      text += std::format(" -define heic:speed={}", *cfg_.magick_speed);
    }
    for (const auto& define : cfg_.magick_defines) {
      text += std::format(" -define {}", utf8_from_wide(define));
    }
    if (cfg_.max_resolution > 0) {
      text += std::format(" -resize {}x{}>", cfg_.max_resolution,
                          cfg_.max_resolution);
    }
    if (cfg_.strip_metadata) {
      text += " -strip";
    }
    return text;
  }

  const AppConfig& cfg_;
  const MagickRuntime& runtime_;
  FileLogger& logger_;
};

}  // namespace avif
