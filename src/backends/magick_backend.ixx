module;

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <chrono>
#include <expected>
#include <filesystem>
#include <format>
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
  fs::path executable{};
  fs::path root{};
  bool bundled{false};
};

namespace magick_detail {

std::optional<fs::path> existing_file(fs::path path) {
  std::error_code ec;
  if (fs::exists(path, ec) && fs::is_regular_file(path, ec) && !ec) {
    return fs::absolute(path, ec);
  }
  return std::nullopt;
}

std::optional<fs::path> existing_magick(fs::path path) {
  std::error_code ec;
  if (fs::is_directory(path, ec) && !ec) {
    path /= L"magick.exe";
  }
  return existing_file(std::move(path));
}

void collect_ancestor_runtime_candidates(std::vector<fs::path>& candidates,
                                         fs::path start) {
  std::error_code ec;
  start = fs::absolute(start, ec);
  if (ec) {
    return;
  }

  for (auto current = start; !current.empty(); current = current.parent_path()) {
    candidates.push_back(current / L"vendor" / L"imagemagick" / L"magick.exe");
    if (current == current.root_path()) {
      break;
    }
  }
}

std::optional<fs::path> environment_magick() {
  std::wstring buffer(32768, L'\0');
  const DWORD size =
      GetEnvironmentVariableW(L"AVIF_MAGICK", buffer.data(),
                              static_cast<DWORD>(buffer.size()));
  if (size == 0 || size >= buffer.size()) {
    return std::nullopt;
  }
  buffer.resize(size);
  return existing_magick(buffer);
}

std::vector<fs::path> bundled_candidates() {
  std::vector<fs::path> candidates;
  collect_ancestor_runtime_candidates(candidates, fs::current_path());
  collect_ancestor_runtime_candidates(candidates, executable_directory());
  return candidates;
}

}  // namespace magick_detail

void configure_magick_environment(const MagickRuntime& runtime) {
  std::error_code ec;
  const auto coder_dir = runtime.root / L"modules" / L"coders";
  if (!fs::is_directory(coder_dir, ec) || ec) {
    return;
  }

  const auto root = runtime.root.native();
  const auto coder_path = coder_dir.native();
  const auto filter_path = (runtime.root / L"modules" / L"filters").native();
  SetEnvironmentVariableW(L"MAGICK_HOME", root.c_str());
  SetEnvironmentVariableW(L"MAGICK_CONFIGURE_PATH", root.c_str());
  SetEnvironmentVariableW(L"MAGICK_CODER_MODULE_PATH", coder_path.c_str());
  SetEnvironmentVariableW(L"MAGICK_FILTER_MODULE_PATH", filter_path.c_str());
}

std::expected<MagickRuntime, std::string> resolve_magick_runtime(
    const AppConfig& cfg) {
  if (cfg.magick_path_overridden) {
    if (const auto direct = magick_detail::existing_magick(cfg.magick_path)) {
      return MagickRuntime{.executable = *direct,
                           .root = direct->parent_path(),
                           .bundled = false};
    }
    if (const auto from_path = find_executable(cfg.magick_path.native())) {
      return MagickRuntime{.executable = *from_path,
                           .root = from_path->parent_path(),
                           .bundled = false};
    }
    return std::unexpected{
        std::format("未找到指定的 magick.exe: {}", path_to_utf8(cfg.magick_path))};
  }

  if (const auto from_env = magick_detail::environment_magick()) {
    return MagickRuntime{.executable = *from_env,
                         .root = from_env->parent_path(),
                         .bundled = false};
  }

  for (const auto& candidate : magick_detail::bundled_candidates()) {
    if (const auto bundled = magick_detail::existing_file(candidate)) {
      return MagickRuntime{.executable = *bundled,
                           .root = bundled->parent_path(),
                           .bundled = true};
    }
  }

  if (const auto from_path = find_executable(L"magick.exe")) {
    return MagickRuntime{.executable = *from_path,
                         .root = from_path->parent_path(),
                         .bundled = false};
  }

  return std::unexpected{
      "未找到 magick.exe。请确认 vendor/imagemagick 存在，或使用 --magick 指定路径。"};
}

class MagickBackend {
 public:
  MagickBackend(const AppConfig& cfg,
                const MagickRuntime& runtime,
                const ProcessRunner& runner,
                FileLogger& logger)
      : cfg_{cfg}, runtime_{runtime}, runner_{runner}, logger_{logger} {}

  EncodeResult encode(const ImageFile& image) const {
    const auto start = std::chrono::steady_clock::now();
    const auto output = cfg_.output_dir / output_name_for(cfg_, image);
    EncodeResult result{.index = image.index,
                        .input_path = image.path,
                        .output_path = output,
                        .original_bytes = image.bytes,
                        .output_bytes = 0,
                        .quality = cfg_.quality,
                        .speed = cfg_.magick_speed.value_or(-1)};

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

      std::vector<std::wstring> args;
      args.reserve(16 + cfg_.magick_defines.size() * 2);
      args.push_back(L"-quiet");
      args.push_back(image.path.native());
      args.push_back(L"-auto-orient");

      if (cfg_.max_resolution > 0) {
        args.push_back(L"-resize");
        args.push_back(
            std::format(L"{}x{}>", cfg_.max_resolution, cfg_.max_resolution));
      }

      if (cfg_.strip_metadata) {
        args.push_back(L"-strip");
      }

      args.push_back(L"-quality");
      args.push_back(std::to_wstring(cfg_.quality));
      if (cfg_.magick_speed) {
        args.push_back(L"-define");
        args.push_back(std::format(L"heic:speed={}", *cfg_.magick_speed));
      }

      for (const auto& define : cfg_.magick_defines) {
        args.push_back(L"-define");
        args.push_back(define);
      }

      args.push_back(output.native());
      const auto command = command_line_for(runtime_.executable, args);
      result.command = utf8_from_wide(command);
      logger_.info(std::format("encode: {}", result.command));

      const auto timeout = std::chrono::minutes{
          cfg_.encode_timeout_minutes > 0 ? cfg_.encode_timeout_minutes : 30};
      const auto process = runner_.run(command, timeout);
      if (process.timed_out) {
        result.message = "ImageMagick 编码超时。";
        logger_.error(result.message);
        return finish(result, start);
      }
      if (process.exit_code != 0) {
        result.message =
            std::format("ImageMagick 退出码 {}: {}", process.exit_code,
                        process.output.empty() ? "无输出" : process.output);
        logger_.error(result.message);
        return finish(result, start);
      }

      if (!fs::exists(output, ec) || fs::file_size(output, ec) == 0 || ec) {
        result.message = "ImageMagick 已结束，但没有生成有效输出文件。";
        logger_.error(result.message);
        return finish(result, start);
      }

      result.output_bytes = fs::file_size(output, ec);
      result.ok = true;
      result.message = "完成。";
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

  const AppConfig& cfg_;
  const MagickRuntime& runtime_;
  const ProcessRunner& runner_;
  FileLogger& logger_;
};

}  // namespace avif
