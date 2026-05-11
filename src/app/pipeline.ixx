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
#include <unordered_map>
#include <vector>

export module avif.pipeline;

import avif.config;
import avif.core;
import avif.magick_backend;

export namespace avif {

namespace pipeline_detail {

struct WorkGroup {
  std::uintmax_t weight{};
  std::vector<ImageFile> files{};
};

std::string first_line(std::string text) {
  const auto pos = text.find_first_of("\r\n");
  if (pos != std::string::npos) {
    text.resize(pos);
  }
  return text;
}

std::vector<WorkGroup> build_work_groups(const AppConfig& cfg,
                                         const std::vector<ImageFile>& files) {
  std::vector<WorkGroup> groups;
  std::unordered_map<std::wstring, std::size_t> index_by_output;

  for (const auto& image : files) {
    const auto output = cfg.output_dir / output_name_for(cfg, image);
    const auto key = normalized_lower_path_key(output);
    const auto [it, inserted] = index_by_output.emplace(key, groups.size());
    if (inserted) {
      groups.push_back(WorkGroup{});
    }

    auto& group = groups[it->second];
    group.weight += image.bytes;
    group.files.push_back(image);
  }

  // 不同输出路径之间按总大小调度；同一路径内保留扫描顺序，重名时后者覆盖前者。
  std::ranges::sort(groups, [](const WorkGroup& left, const WorkGroup& right) {
    return left.weight > right.weight;
  });
  return groups;
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
    auto work = pipeline_detail::build_work_groups(cfg, files);
    const int jobs = std::max(
        1, std::min<int>(cfg.max_jobs, static_cast<int>(work.size())));
    std::println("Files: {}, Outputs: {}, Jobs: {}", files.size(),
                 work.size(), jobs);

    std::vector<EncodeResult> results(files.size());
    std::atomic<std::size_t> next{0};
    std::mutex print_mutex;

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
          const auto& group = work[work_index];
          if (group.files.size() > 1) {
            std::scoped_lock lock{print_mutex};
            std::println("[WARN] 输出重名: {} 个输入将依次覆盖 {}",
                         group.files.size(),
                         path_to_utf8(cfg.output_dir /
                                      output_name_for(cfg, group.files.back())));
          }
          for (const auto& image : group.files) {
            auto result = backend.encode(image);
            pipeline_detail::print_result(result, print_mutex);
            results[result.index] = std::move(result);
          }
        }
      });
    }

    workers.clear();

    std::uintmax_t original_total = 0;
    std::unordered_map<std::wstring, std::uintmax_t> final_output_sizes;
    int ok_count = 0;
    int failed_count = 0;
    for (const auto& result : results) {
      original_total += result.original_bytes;
      if (result.ok) {
        ++ok_count;
        final_output_sizes[normalized_lower_path_key(result.output_path)] =
            result.output_bytes;
      } else {
        ++failed_count;
      }
    }
    std::uintmax_t output_total = 0;
    for (const auto& [_, bytes] : final_output_sizes) {
      output_total += bytes;
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
