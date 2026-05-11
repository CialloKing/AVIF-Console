module;

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <exception>
#include <filesystem>
#include <format>
#include <mutex>
#include <print>
#include <ranges>
#include <string>
#include <thread>
#include <vector>

export module avif.pipeline;

import avif.config;
import avif.core;
import avif.magick_backend;

export namespace avif {

namespace pipeline_detail {

std::string first_line(std::string text) {
  const auto pos = text.find_first_of("\r\n");
  if (pos != std::string::npos) {
    text.resize(pos);
  }
  return text;
}

void print_result(const EncodeResult& result, std::mutex& print_mutex) {
  std::scoped_lock lock{print_mutex};
  if (result.ok) {
    if (result.skipped) {
      std::println("[SKIP] {:04} {} -> 已存在", result.index + 1,
                   path_to_utf8(result.input_path.filename()));
      return;
    }

    const double ratio =
        result.original_bytes == 0
            ? 0.0
            : static_cast<double>(result.output_bytes) /
                  static_cast<double>(result.original_bytes);
    std::println("[ OK ] {:04} {} -> {} ({}, {:.1f}%, {:.2f}s)",
                 result.index + 1, path_to_utf8(result.input_path.filename()),
                 path_to_utf8(result.output_path.filename()),
                 format_size(result.output_bytes), ratio * 100.0,
                 result.seconds);
    return;
  }

  std::println("[FAIL] {:04} {} -> {}", result.index + 1,
               path_to_utf8(result.input_path.filename()), result.message);
}

}  // namespace pipeline_detail

// 顶层流水线返回进程退出码；单张图片错误会落到 CSV，不会让程序闪退。
int run_pipeline(const AppConfig& cfg) {
  try {
    const auto runtime = resolve_magick_runtime(cfg);
    if (!runtime) {
      std::println("[FAIL] {}", runtime.error());
      return 1;
    }

    configure_magick_environment(*runtime);
    fs::create_directories(cfg.output_dir);

    FileLogger logger{cfg.output_dir};
    ProcessRunner runner;

    const std::vector<std::wstring> version_args{L"-version"};
    const auto version =
        runner.run(runtime->executable, version_args, std::chrono::seconds{15});
    if (version.exit_code != 0) {
      std::println("[FAIL] magick.exe 无法启动: {}",
                   version.output.empty() ? "无输出" : version.output);
      return 1;
    }

    logger.info(std::format("magick: {}", path_to_utf8(runtime->executable)));
    logger.info(pipeline_detail::first_line(version.output));

    auto files = scan_images(cfg);
    if (files.empty()) {
      std::println("未找到图片。支持: jpg, jpeg, png, webp, bmp, tif, tiff, gif, jxl, jp2, heic, heif");
      return 0;
    }

    std::println("ImageMagick: {}", path_to_utf8(runtime->executable));
    std::println("Runtime: {}", runtime->bundled ? "vendor/imagemagick" : "external");
    std::println("Quality: q{}", cfg.quality);
    if (cfg.magick_speed) {
      std::println("Speed: heic:speed={}", *cfg.magick_speed);
    } else {
      std::println("Speed: ImageMagick default");
    }
    std::println("Files: {}, Jobs: {}", files.size(),
                 std::min<int>(cfg.max_jobs, static_cast<int>(files.size())));

    auto work = files;
    std::ranges::sort(work, [](const ImageFile& left, const ImageFile& right) {
      return left.bytes > right.bytes;
    });

    std::vector<EncodeResult> results(files.size());
    std::atomic<std::size_t> next{0};
    std::mutex print_mutex;
    const int jobs = std::max(
        1, std::min<int>(cfg.max_jobs, static_cast<int>(work.size())));

    std::vector<std::jthread> workers;
    workers.reserve(static_cast<std::size_t>(jobs));
    for (int i = 0; i < jobs; ++i) {
      workers.emplace_back([&] {
        MagickBackend backend{cfg, *runtime, runner, logger};
        while (true) {
          const auto work_index = next.fetch_add(1);
          if (work_index >= work.size()) {
            break;
          }
          auto result = backend.encode(work[work_index]);
          pipeline_detail::print_result(result, print_mutex);
          results[result.index] = std::move(result);
        }
      });
    }

    workers.clear();

    std::uintmax_t original_total = 0;
    std::uintmax_t output_total = 0;
    int ok_count = 0;
    int failed_count = 0;
    for (const auto& result : results) {
      original_total += result.original_bytes;
      output_total += result.output_bytes;
      if (result.ok) {
        ++ok_count;
      } else {
        ++failed_count;
      }
    }

    write_csv(cfg.output_dir, results);
    const double total_ratio =
        original_total == 0
            ? 0.0
            : static_cast<double>(output_total) /
                  static_cast<double>(original_total);

    std::println("");
    std::println("完成: 成功 {}，失败 {}", ok_count, failed_count);
    std::println("体积: {} -> {} ({:.1f}%)", format_size(original_total),
                 format_size(output_total), total_ratio * 100.0);
    std::println("报告: {}", path_to_utf8(cfg.output_dir / L"summary.csv"));
    return failed_count == 0 ? 0 : 2;
  } catch (const std::exception& ex) {
    std::println("[FAIL] {}", ex.what());
    return 1;
  } catch (...) {
    std::println("[FAIL] 未知异常，程序已安全退出。");
    return 1;
  }
}

}  // namespace avif
