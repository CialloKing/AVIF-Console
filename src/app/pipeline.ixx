module;

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <exception>
#include <filesystem>
#include <format>
#include <functional>
#include <mutex>
#include <optional>
#include <print>
#include <ranges>
#include <stop_token>
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

std::vector<WorkGroup> build_work_groups(const AppConfig& cfg,
                                         const std::vector<ImageFile>& files) {
  std::vector<WorkGroup> groups;
  std::unordered_map<std::wstring, std::size_t> index_by_output;
  const auto output_dir = output_dir_for(cfg);

  for (const auto& image : files) {
    const auto output = output_dir / output_name_for(cfg, image);
    auto key = normalized_lower_path_key(output);
    if (cfg.collision_mode == CollisionMode::suffix_time ||
        cfg.collision_mode == CollisionMode::suffix_random) {
      key += std::format(L"#{}", image.index);
    }
    const auto [it, inserted] = index_by_output.emplace(key, groups.size());
    if (inserted) {
      groups.push_back(WorkGroup{});
    }

    auto& group = groups[it->second];
    group.weight += image.bytes;
    group.files.push_back(image);
  }

  // 不同输出路径之间按总大小调度；覆盖/跳过模式下同一路径保留扫描顺序。
  std::ranges::sort(groups, [](const WorkGroup& left, const WorkGroup& right) {
    return left.weight > right.weight;
  });
  return groups;
}

std::string format_result_line(const EncodeResult& result) {
  if (result.ok) {
    if (result.skipped) {
      return std::format("[SKIP] {:04} {} -> 已存在", result.index + 1,
                         path_to_utf8(result.input_path.filename()));
    }

    const double ratio =
        result.original_bytes == 0
            ? 0.0
            : static_cast<double>(result.output_bytes) /
                  static_cast<double>(result.original_bytes);
    return std::format("[ OK ] {:04} {} -> {} ({}, {:.1f}%, {:.2f}s)",
                       result.index + 1,
                       path_to_utf8(result.input_path.filename()),
                       path_to_utf8(result.output_path.filename()),
                       format_size(result.output_bytes), ratio * 100.0,
                       result.seconds);
  }

  return std::format("[FAIL] {:04} {} -> {}", result.index + 1,
                     path_to_utf8(result.input_path.filename()), result.message);
}

}  // namespace pipeline_detail

enum class BatchEventKind { message, warning, item_finished, summary };

struct BatchSummary {
  int ok_count{};
  int failed_count{};
  std::uintmax_t original_total{};
  std::uintmax_t output_total{};
  bool canceled{};
  int exit_code{};
};

struct BatchProgress {
  BatchEventKind kind{BatchEventKind::message};
  std::size_t completed{};
  std::size_t total{};
  EncodeResult result{};
  BatchSummary summary{};
  std::string text{};
};

using ProgressCallback = std::function<void(const BatchProgress&)>;

void emit_progress(const ProgressCallback& progress, BatchProgress event) {
  try {
    if (progress) {
      progress(event);
    }
  } catch (...) {
    // 进度回调不应该影响编码任务；UI 层异常会被吞掉，批处理继续写 CSV。
  }
}

std::expected<BatchSummary, std::string> run_batch(
    const AppConfig& cfg,
    ProgressCallback progress = {},
    std::stop_token stop_token = {}) {
  try {
    const auto runtime = resolve_magick_runtime(cfg);
    if (!runtime) {
      return std::unexpected{runtime.error()};
    }

    configure_magick_environment(*runtime);
    const auto output_dir = output_dir_for(cfg);
    fs::create_directories(output_dir);

    FileLogger logger{output_dir, cfg.write_log};
    logger.info(std::format("imagemagick runtime: {}", path_to_utf8(runtime->root)));

    auto files = scan_images(cfg);
    if (files.empty()) {
      emit_progress(progress, BatchProgress{
                                  .kind = BatchEventKind::message,
                                  .text =
                                      "未找到图片。支持: jpg, jpeg, png, webp, bmp, "
                                      "tif, tiff, gif, jxl, jp2, heic, heif, avif"});
      return BatchSummary{.exit_code = 0};
    }

    emit_progress(progress, BatchProgress{
                                .kind = BatchEventKind::message,
                                .total = files.size(),
                                .text = std::format("ImageMagick runtime: {}",
                                                    path_to_utf8(runtime->root))});
    emit_progress(progress, BatchProgress{
                                .kind = BatchEventKind::message,
                                .total = files.size(),
                                .text = std::format("Runtime: {}",
                                                    runtime->bundled ? "bundled"
                                                                     : "external")});
    emit_progress(progress, BatchProgress{.kind = BatchEventKind::message,
                                          .total = files.size(),
                                          .text = std::format("Quality: q{}",
                                                              cfg.quality)});
    emit_progress(progress, BatchProgress{
                                .kind = BatchEventKind::message,
                                .total = files.size(),
                                .text = cfg.output_format == OutputFormat::avif
                                            ? (cfg.magick_speed
                                                   ? std::format(
                                                         "Speed: heic:speed={}",
                                                         *cfg.magick_speed)
                                                   : "Speed: ImageMagick default")
                                            : "Speed: WebP uses ImageMagick defaults"});

    auto work = pipeline_detail::build_work_groups(cfg, files);
    const int jobs = std::max(
        1, std::min<int>(cfg.max_jobs, static_cast<int>(work.size())));
    emit_progress(progress, BatchProgress{
                                .kind = BatchEventKind::message,
                                .total = files.size(),
                                .text = std::format("Files: {}, Outputs: {}, Jobs: {}",
                                                    files.size(), work.size(), jobs)});

    std::vector<EncodeResult> results(files.size());
    for (const auto& image : files) {
      results[image.index] = EncodeResult{.index = image.index,
                                          .input_path = image.path,
                                          .output_path = output_dir /
                                                         output_name_for(cfg, image),
                                          .original_bytes = image.bytes,
                                          .quality = cfg.quality,
                                          .speed = cfg.magick_speed.value_or(-1),
                                          .message = "未处理。"};
    }

    std::atomic<std::size_t> next{0};
    std::atomic<std::size_t> completed{0};

    std::vector<std::jthread> workers;
    workers.reserve(static_cast<std::size_t>(jobs));
    for (int i = 0; i < jobs; ++i) {
      workers.emplace_back([&] {
        MagickBackend backend{cfg, *runtime, logger};
        while (true) {
          if (stop_token.stop_requested()) {
            break;
          }

          const auto work_index = next.fetch_add(1);
          if (work_index >= work.size()) {
            break;
          }
          const auto& group = work[work_index];
          if (group.files.size() > 1 &&
              cfg.collision_mode == CollisionMode::overwrite) {
            emit_progress(progress, BatchProgress{
                                        .kind = BatchEventKind::warning,
                                        .completed = completed.load(),
                                        .total = files.size(),
                                        .text = std::format(
                                            "[WARN] 输出重名: {} 个输入将依次覆盖 {}",
                                            group.files.size(),
                                            path_to_utf8(
                                                output_dir /
                                                output_name_for(
                                                    cfg, group.files.back())))});
          }
          for (const auto& image : group.files) {
            if (stop_token.stop_requested()) {
              break;
            }
            auto result = backend.encode(image);
            const auto event_result = result;
            results[result.index] = std::move(result);
            const auto done = completed.fetch_add(1) + 1;
            emit_progress(progress, BatchProgress{
                                        .kind = BatchEventKind::item_finished,
                                        .completed = done,
                                        .total = files.size(),
                                        .result = event_result,
                                        .text = pipeline_detail::format_result_line(
                                            event_result)});
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

    if (cfg.write_summary) {
      write_csv(output_dir, results);
    }
    const double total_ratio =
        original_total == 0
            ? 0.0
            : static_cast<double>(output_total) /
                  static_cast<double>(original_total);

    const bool canceled = stop_token.stop_requested();
    BatchSummary summary{.ok_count = ok_count,
                         .failed_count = failed_count,
                         .original_total = original_total,
                         .output_total = output_total,
                         .canceled = canceled,
                         .exit_code = canceled ? 130 : (failed_count == 0 ? 0 : 2)};
    emit_progress(progress, BatchProgress{
                                .kind = BatchEventKind::summary,
                                .completed = completed.load(),
                                .total = files.size(),
                                .summary = summary,
                                .text = std::format(
                                    "{}: 成功 {}，失败 {}\n体积: {} -> {} ({:.1f}%){}",
                                    canceled ? "已取消" : "完成", ok_count,
                                    failed_count, format_size(original_total),
                                    format_size(output_total), total_ratio * 100.0,
                                    cfg.write_summary
                                        ? std::format("\n报告: {}",
                                                      path_to_utf8(output_dir /
                                                                   L"summary.csv"))
                                        : "")});
    return summary;
  } catch (const std::exception& ex) {
    return std::unexpected{std::string{ex.what()}};
  } catch (...) {
    return std::unexpected{"未知异常，程序已安全退出。"};
  }
}

// 顶层流水线返回进程退出码；单张图片错误会落到 CSV，不会让程序闪退。
int run_pipeline(const AppConfig& cfg) {
  std::mutex print_mutex;
  const auto summary = run_batch(
      cfg,
      [&](const BatchProgress& event) {
        std::scoped_lock lock{print_mutex};
        if (event.kind == BatchEventKind::summary) {
          std::println("");
        }
        std::println("{}", event.text);
      });
  if (!summary) {
    std::println("[FAIL] {}", summary.error());
    return 1;
  }
  return summary->exit_code;
}

}  // namespace avif
