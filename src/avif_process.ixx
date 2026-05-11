module;

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <ctime>
#include <cwctype>
#include <cstdint>
#include <exception>
#include <filesystem>
#include <format>
#include <fstream>
#include <iomanip>
#include <ios>
#include <mutex>
#include <numeric>
#include <optional>
#include <print>
#include <regex>
#include <sstream>
#include <string>
#include <string_view>
#include <thread>
#include <vector>
#include <windows.h>

export module avif.process;

import avif.config;

namespace avif {
namespace fs = std::filesystem;

struct ProcessResult {
  int exit_code{-1};
  bool timed_out{false};
  std::string output{};
};

std::string utf8_from_wide(std::wstring_view text) {
  if (text.empty()) {
    return {};
  }
  const auto needed =
      WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                          nullptr, 0, nullptr, nullptr);
  std::string out(static_cast<std::size_t>(needed), '\0');
  WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                      out.data(), needed, nullptr, nullptr);
  return out;
}

std::wstring wide_from_utf8(std::string_view text) {
  if (text.empty()) {
    return {};
  }
  const auto needed =
      MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                          nullptr, 0);
  std::wstring out(static_cast<std::size_t>(needed), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                      out.data(), needed);
  return out;
}

std::string path_to_utf8(const fs::path& path) {
  return utf8_from_wide(path.native());
}

std::string win32_error_message(DWORD code) {
  wchar_t* raw = nullptr;
  const auto size = FormatMessageW(
      FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM |
          FORMAT_MESSAGE_IGNORE_INSERTS,
      nullptr, code, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
      reinterpret_cast<LPWSTR>(&raw), 0, nullptr);
  std::wstring message = size == 0 ? L"unknown Win32 error"
                                   : std::wstring{raw, raw + size};
  if (raw != nullptr) {
    LocalFree(raw);
  }
  while (!message.empty() &&
         (message.back() == L'\n' || message.back() == L'\r' ||
          message.back() == L' ')) {
    message.pop_back();
  }
  return utf8_from_wide(message);
}

std::wstring quote_arg(std::wstring_view arg) {
  if (arg.empty()) {
    return L"\"\"";
  }

  const auto needs_quotes =
      arg.find_first_of(L" \t\n\v\"") != std::wstring_view::npos;
  if (!needs_quotes) {
    return std::wstring{arg};
  }

  std::wstring result;
  result.push_back(L'"');
  std::size_t backslashes = 0;
  for (const auto ch : arg) {
    if (ch == L'\\') {
      ++backslashes;
    } else if (ch == L'"') {
      result.append(backslashes * 2 + 1, L'\\');
      result.push_back(ch);
      backslashes = 0;
    } else {
      result.append(backslashes, L'\\');
      backslashes = 0;
      result.push_back(ch);
    }
  }
  result.append(backslashes * 2, L'\\');
  result.push_back(L'"');
  return result;
}

std::wstring command_line_for(const fs::path& exe,
                              const std::vector<std::wstring>& args) {
  std::wstring command = quote_arg(exe.native());
  for (const auto& arg : args) {
    command.push_back(L' ');
    command += quote_arg(arg);
  }
  return command;
}

std::optional<fs::path> find_executable(std::wstring_view name) {
  std::array<wchar_t, 32768> buffer{};
  const auto length =
      SearchPathW(nullptr, std::wstring{name}.c_str(), nullptr,
                  static_cast<DWORD>(buffer.size()), buffer.data(), nullptr);
  if (length == 0 || length >= buffer.size()) {
    return std::nullopt;
  }
  return fs::path{std::wstring_view{buffer.data(), length}};
}

// 小型 Win32 进程封装：统一捕获 stdout/stderr，并用 jthread 读取管道。
class ProcessRunner {
 public:
  ProcessResult run(std::wstring command,
                    std::chrono::milliseconds timeout) const {
    SECURITY_ATTRIBUTES security{};
    security.nLength = sizeof(security);
    security.bInheritHandle = TRUE;

    HANDLE read_pipe = nullptr;
    HANDLE write_pipe = nullptr;
    if (!CreatePipe(&read_pipe, &write_pipe, &security, 0)) {
      return {.output = win32_error_message(GetLastError())};
    }
    SetHandleInformation(read_pipe, HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdOutput = write_pipe;
    startup.hStdError = write_pipe;
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);

    PROCESS_INFORMATION process{};
    auto mutable_command = std::move(command);
    const BOOL created =
        CreateProcessW(nullptr, mutable_command.data(), nullptr, nullptr, TRUE,
                       CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process);
    CloseHandle(write_pipe);

    if (!created) {
      const auto message = win32_error_message(GetLastError());
      CloseHandle(read_pipe);
      return {.output = message};
    }

    std::string output;
    // jthread 在作用域退出时自动 join，避免异常路径遗留读管道线程。
    std::jthread reader([&] {
      std::array<char, 4096> buffer{};
      DWORD bytes_read = 0;
      while (ReadFile(read_pipe, buffer.data(), static_cast<DWORD>(buffer.size()),
                      &bytes_read, nullptr) &&
             bytes_read > 0) {
        output.append(buffer.data(), buffer.data() + bytes_read);
      }
    });

    const auto wait_ms =
        timeout.count() <= 0
            ? INFINITE
            : static_cast<DWORD>(
                  std::min<std::int64_t>(timeout.count(), INFINITE - 1));
    const auto wait_result = WaitForSingleObject(process.hProcess, wait_ms);

    bool timed_out = false;
    if (wait_result == WAIT_TIMEOUT) {
      timed_out = true;
      TerminateProcess(process.hProcess, 124);
      WaitForSingleObject(process.hProcess, INFINITE);
    }

    reader.join();
    CloseHandle(read_pipe);

    DWORD exit_code = 0;
    GetExitCodeProcess(process.hProcess, &exit_code);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return {.exit_code = static_cast<int>(exit_code),
            .timed_out = timed_out,
            .output = std::move(output)};
  }
};

std::string now_string() {
  const auto now = std::chrono::system_clock::now();
  const auto time = std::chrono::system_clock::to_time_t(now);
  std::tm local{};
  ::localtime_s(&local, &time);
  std::array<char, 32> buffer{};
  std::strftime(buffer.data(), buffer.size(), "%Y-%m-%d %H:%M:%S", &local);
  return buffer.data();
}

class FileLogger {
 public:
  explicit FileLogger(fs::path output_dir)
      : log_dir_{std::move(output_dir) / L"log"} {
    fs::create_directories(log_dir_);
    info("===== NEW SESSION START =====");
  }

  void info(std::string_view message) { write("run.log", "INFO", message); }
  void error(std::string_view message) { write("error.log", "ERROR", message); }
  void search(std::string_view message) {
    write("crf_search.log", "SEARCH", message);
  }

 private:
  fs::path log_dir_;
  std::mutex mutex_;

  void write(std::string_view file, std::string_view level,
             std::string_view message) {
    const auto path = log_dir_ / wide_from_utf8(std::string{file});
    std::lock_guard lock{mutex_};
    std::ofstream out(path, std::ios::app | std::ios::binary);
    out << '[' << now_string() << "] [" << level << "] " << message << '\n';
  }
};

struct ProbeInfo {
  int width{};
  int height{};
  std::string pixel_format{"unknown"};
  bool has_alpha{};
};

struct ImageFile {
  fs::path path;
  int index{};
  std::uintmax_t size{};
};

struct EncodeResult {
  int index{};
  fs::path input_path;
  fs::path output_path;
  std::uintmax_t original_size{};
  std::uintmax_t output_size{};
  int crf{};
  double ssim{-1.0};
  double encode_seconds{};
  double total_seconds{};
  bool success{false};
  bool skipped{false};
  std::string pixel_format;
  std::string error;
  std::string command_line;
};

std::string lower_ascii(std::string text) {
  std::ranges::transform(text, text.begin(), [](unsigned char ch) {
    return static_cast<char>(std::tolower(ch));
  });
  return text;
}

std::wstring lower_wide(std::wstring text) {
  std::ranges::transform(text, text.begin(),
                         [](wchar_t ch) { return std::towlower(ch); });
  return text;
}

bool contains(std::string_view haystack, std::string_view needle) {
  return haystack.find(needle) != std::string_view::npos;
}

bool starts_with(std::string_view text, std::string_view prefix) {
  return text.starts_with(prefix);
}

bool is_libaom(std::string_view encoder) { return encoder == "libaom-av1"; }
bool is_svt(std::string_view encoder) { return encoder == "libsvtav1"; }
bool is_rav1e(std::string_view encoder) { return encoder == "librav1e"; }

std::vector<std::string> split_csv_line(std::string_view line) {
  std::vector<std::string> parts;
  std::string current;
  for (const auto ch : line) {
    if (ch == ',') {
      parts.push_back(std::move(current));
      current.clear();
    } else {
      current.push_back(ch);
    }
  }
  parts.push_back(std::move(current));
  return parts;
}

std::optional<ProbeInfo> probe_image(const ProcessRunner& runner,
                                     const fs::path& ffprobe,
                                     const fs::path& image,
                                     FileLogger& logger) {
  const auto command =
      command_line_for(ffprobe, {L"-v", L"error", L"-select_streams", L"v:0",
                                 L"-show_entries",
                                 L"stream=width,height,pix_fmt", L"-of",
                                 L"csv=p=0", image.native()});
  const auto result = runner.run(command, std::chrono::seconds{30});
  if (result.exit_code != 0) {
    logger.error(std::format("ffprobe failed for {}: {}", path_to_utf8(image),
                             result.output));
    return std::nullopt;
  }

  std::istringstream stream{result.output};
  std::string first_line;
  std::getline(stream, first_line);
  const auto parts = split_csv_line(first_line);
  if (parts.size() < 3) {
    logger.error(std::format("ffprobe output is not understood: {}",
                             result.output));
    return std::nullopt;
  }

  ProbeInfo info;
  try {
    info.width = std::stoi(parts[0]);
    info.height = std::stoi(parts[1]);
  } catch (...) {
    return std::nullopt;
  }
  info.pixel_format = lower_ascii(parts[2]);
  info.has_alpha = contains(info.pixel_format, "rgba") ||
                   contains(info.pixel_format, "bgra") ||
                   contains(info.pixel_format, "argb") ||
                   contains(info.pixel_format, "yuva") ||
                   contains(info.pixel_format, "gbrap") ||
                   starts_with(info.pixel_format, "ya");
  return info;
}

Chroma source_chroma(const ProbeInfo& probe) {
  const auto fmt = probe.pixel_format;
  if (contains(fmt, "444") || contains(fmt, "rgb") || contains(fmt, "gbr")) {
    return Chroma::yuv444;
  }
  if (contains(fmt, "422")) {
    return Chroma::yuv422;
  }
  return Chroma::yuv420;
}

int source_bit_depth(const ProbeInfo& probe, const AppConfig& cfg) {
  if (!cfg.auto_source || cfg.user_set_bit_depth) {
    return cfg.bit_depth;
  }
  const auto fmt = probe.pixel_format;
  if (contains(fmt, "10") || contains(fmt, "12") || contains(fmt, "16")) {
    return 10;
  }
  return 8;
}

std::string pixel_format_for(const AppConfig& cfg, const ProbeInfo& probe) {
  const auto chroma = cfg.auto_source ? source_chroma(probe) : cfg.chroma;
  const auto bit_depth = cfg.lossless ? 10 : source_bit_depth(probe, cfg);
  const auto prefix = probe.has_alpha ? std::string{"yuva"} : std::string{"yuv"};

  std::string suffix;
  switch (chroma) {
    case Chroma::auto_source:
    case Chroma::yuv420:
      suffix = "420";
      break;
    case Chroma::yuv422:
      suffix = "422";
      break;
    case Chroma::yuv444:
      suffix = "444";
      break;
  }

  return prefix + suffix + (bit_depth == 10 ? "p10le" : "p");
}

std::wstring replace_all(std::wstring text, std::wstring_view from,
                         std::wstring_view to) {
  std::size_t pos = 0;
  while ((pos = text.find(from, pos)) != std::wstring::npos) {
    text.replace(pos, from.size(), to);
    pos += to.size();
  }
  return text;
}

std::wstring two_digit_index(int index) {
  std::wostringstream out;
  out << std::setw(2) << std::setfill(L'0') << index;
  return out.str();
}

bool ends_with_avif(std::wstring text) {
  text = lower_wide(std::move(text));
  return text.ends_with(L".avif");
}

std::wstring sanitize_filename(std::wstring name) {
  static constexpr std::wstring_view invalid = L"<>:\"/\\|?*";
  for (auto& ch : name) {
    if (ch < 32 || invalid.find(ch) != std::wstring_view::npos) {
      ch = L'_';
    }
  }
  while (!name.empty() && (name.back() == L' ' || name.back() == L'.')) {
    name.pop_back();
  }
  return name.empty() ? L"output.avif" : name;
}

std::wstring output_name_for(const AppConfig& cfg, const ImageFile& image) {
  const auto stem = image.path.stem().native();
  const auto index = two_digit_index(image.index);
  auto result = cfg.output_template;
  result = replace_all(std::move(result), L"{{name}}", stem);
  result = replace_all(std::move(result), L"{name}", stem);
  result = replace_all(std::move(result), L"{{filename}}", stem);
  result = replace_all(std::move(result), L"{filename}", stem);
  result = replace_all(std::move(result), L"{{index}}", index);
  result = replace_all(std::move(result), L"{index}", index);
  if (!ends_with_avif(result)) {
    result += L".avif";
  }
  return sanitize_filename(std::move(result));
}

std::wstring scaling_filter(int max_resolution) {
  return wide_from_utf8(std::format(
      "scale='if(gt(iw,ih),min(iw,{}),-2)':'if(gt(ih,iw),min(ih,{}),-2)'",
      max_resolution, max_resolution));
}

void append_ascii(std::vector<std::wstring>& args, std::string_view value) {
  args.push_back(wide_from_utf8(value));
}

int svt_preset_from_cpu_used(int cpu_used) {
  return std::clamp(cpu_used * 13 / 8, 0, 13);
}

void append_quality_args(std::vector<std::wstring>& args,
                         std::string_view encoder,
                         bool lossless,
                         int crf) {
  if (lossless && is_libaom(encoder)) {
    args.emplace_back(L"-lossless");
    args.emplace_back(L"1");
    return;
  }

  if (starts_with(encoder, "av1_qsv") || starts_with(encoder, "av1_vaapi")) {
    args.emplace_back(L"-global_quality");
    args.push_back(std::to_wstring(crf));
  } else if (starts_with(encoder, "av1_nvenc") ||
             starts_with(encoder, "av1_amf") ||
             starts_with(encoder, "av1_vulkan")) {
    args.emplace_back(L"-qp");
    args.push_back(std::to_wstring(crf));
  } else {
    args.emplace_back(L"-crf");
    args.push_back(std::to_wstring(crf));
  }
}

void append_encoder_specific_args(std::vector<std::wstring>& args,
                                  const AppConfig& cfg,
                                  int cpu_used,
                                  bool search_encode) {
  const auto encoder = lower_ascii(cfg.encoder);
  if (is_libaom(encoder)) {
    args.emplace_back(L"-cpu-used");
    args.push_back(std::to_wstring(std::clamp(cpu_used, 0, 8)));
    args.emplace_back(L"-row-mt");
    args.emplace_back(L"1");
    args.emplace_back(L"-still-picture");
    args.emplace_back(L"1");
    if (!cfg.lossless && !search_encode && !cfg.aom_params.empty()) {
      args.emplace_back(L"-aom-params");
      append_ascii(args, cfg.aom_params);
    }
    return;
  }

  if (is_svt(encoder)) {
    args.emplace_back(L"-preset");
    args.push_back(std::to_wstring(svt_preset_from_cpu_used(cpu_used)));
    if (!cfg.lossless) {
      args.emplace_back(L"-svtav1-params");
      args.emplace_back(L"scd=0:aq-mode=2:enable-tpl-la=1:enable-mfmv=1");
    }
    return;
  }

  if (is_rav1e(encoder)) {
    args.emplace_back(L"-speed");
    args.push_back(std::to_wstring(std::clamp(cpu_used, 0, 10)));
  }
}

std::vector<std::wstring> build_encode_args(const fs::path& input,
                                            const fs::path& output,
                                            const AppConfig& cfg,
                                            std::string_view pixel_format,
                                            int crf,
                                            int cpu_used,
                                            bool search_encode) {
  std::vector<std::wstring> args{L"-hide_banner", L"-loglevel", L"error",
                                 L"-y",           L"-i",       input.native(),
                                 L"-map_metadata", L"0"};

  if (cfg.max_resolution > 0 && !cfg.output_full_res) {
    args.emplace_back(L"-vf");
    args.push_back(scaling_filter(cfg.max_resolution));
  }

  args.emplace_back(L"-frames:v");
  args.emplace_back(L"1");
  args.emplace_back(L"-c:v");
  append_ascii(args, cfg.encoder);
  args.emplace_back(L"-pix_fmt");
  append_ascii(args, pixel_format);
  args.emplace_back(L"-color_range");
  args.emplace_back(L"pc");
  args.emplace_back(L"-color_primaries");
  args.emplace_back(L"bt709");
  args.emplace_back(L"-color_trc");
  args.emplace_back(L"iec61966-2-1");
  args.emplace_back(L"-colorspace");
  args.emplace_back(L"bt709");

  append_quality_args(args, lower_ascii(cfg.encoder), cfg.lossless, crf);
  append_encoder_specific_args(args, cfg, cpu_used, search_encode);
  args.push_back(output.native());
  return args;
}

int automatic_timeout_minutes(const ProbeInfo& probe) {
  const auto pixels = static_cast<std::int64_t>(probe.width) * probe.height;
  const auto minutes = static_cast<int>(std::ceil(pixels / 2'000'000.0 * 5.0));
  return std::clamp(minutes, 5, 180);
}

struct EncodeAttempt {
  bool ok{};
  double seconds{};
  std::string error;
  std::string command;
};

EncodeAttempt encode_to_file(const ProcessRunner& runner,
                             const fs::path& ffmpeg,
                             const fs::path& input,
                             const fs::path& output,
                             const AppConfig& cfg,
                             const ProbeInfo& probe,
                             std::string_view pixel_format,
                             int crf,
                             int cpu_used,
                             bool search_encode,
                             FileLogger& logger) {
  const auto args = build_encode_args(input, output, cfg, pixel_format, crf,
                                      cpu_used, search_encode);
  const auto command = command_line_for(ffmpeg, args);
  const auto timeout = std::chrono::minutes{
      cfg.encode_timeout_minutes > 0 ? cfg.encode_timeout_minutes
                                     : automatic_timeout_minutes(probe)};
  const auto start = std::chrono::steady_clock::now();
  const auto result = runner.run(command, timeout);
  const auto elapsed = std::chrono::duration<double>(
                           std::chrono::steady_clock::now() - start)
                           .count();

  const auto ok = result.exit_code == 0 && fs::exists(output) &&
                  fs::file_size(output) > 0;
  if (!ok) {
    logger.error(std::format("ffmpeg failed for {}: {}", path_to_utf8(input),
                             result.timed_out ? "timeout" : result.output));
  }
  return {.ok = ok,
          .seconds = elapsed,
          .error = result.timed_out ? "ffmpeg 超时" : result.output,
          .command = utf8_from_wide(command)};
}

std::optional<double> parse_ssim(std::string_view output) {
  static const std::regex pattern{R"(All:([0-9]+(?:\.[0-9]+)?))"};
  std::cmatch match;
  if (std::regex_search(output.data(), output.data() + output.size(), match,
                        pattern)) {
    try {
      return std::stod(match[1].str());
    } catch (...) {
      return std::nullopt;
    }
  }
  return std::nullopt;
}

std::optional<double> measure_ssim(const ProcessRunner& runner,
                                   const fs::path& ffmpeg,
                                   const fs::path& original,
                                   const fs::path& encoded,
                                   FileLogger& logger) {
  const std::vector<std::wstring> args{
      L"-hide_banner",
      L"-loglevel",
      L"info",
      L"-i",
      encoded.native(),
      L"-i",
      original.native(),
      L"-filter_complex",
      L"[1:v][0:v]scale2ref[ref][dist];[dist][ref]ssim",
      L"-f",
      L"null",
      L"-"};
  const auto command = command_line_for(ffmpeg, args);
  const auto result = runner.run(command, std::chrono::minutes{5});
  if (result.exit_code != 0) {
    logger.error(std::format("SSIM failed for {}: {}", path_to_utf8(encoded),
                             result.output));
    return std::nullopt;
  }
  return parse_ssim(result.output);
}

int search_crf(const ProcessRunner& runner,
               const fs::path& ffmpeg,
               const fs::path& input,
               const fs::path& temp_dir,
               const AppConfig& cfg,
               const ProbeInfo& probe,
               std::string_view pixel_format,
               FileLogger& logger) {
  int low = cfg.min_crf;
  int high = cfg.max_crf;
  int best = cfg.base_crf;
  double best_score = -1.0;

  while (low <= high) {
    const auto mid = low + (high - low) / 2;
    const auto temp =
        temp_dir / std::format(L"_probe_{}_{}.avif",
                               std::hash<std::wstring>{}(input.native()), mid);

    const auto attempt = encode_to_file(runner, ffmpeg, input, temp, cfg, probe,
                                        pixel_format, mid,
                                        cfg.search_cpu_used, true, logger);
    if (!attempt.ok) {
      high = mid - 1;
      fs::remove(temp);
      continue;
    }

    const auto score = measure_ssim(runner, ffmpeg, input, temp, logger);
    fs::remove(temp);
    if (!score) {
      high = mid - 1;
      continue;
    }

    logger.search(std::format("{} CRF={} SSIM={:.5f}", path_to_utf8(input), mid,
                              *score));
    if (*score >= cfg.target_ssim) {
      best = mid;
      best_score = *score;
      low = mid + 1;
    } else {
      high = mid - 1;
    }
  }

  if (best_score < 0.0) {
    logger.search(std::format("{} 搜索失败，回退 CRF={}", path_to_utf8(input),
                              cfg.base_crf));
    return cfg.base_crf;
  }
  logger.search(std::format("{} 最佳 CRF={} SSIM={:.5f}", path_to_utf8(input),
                            best, best_score));
  return best;
}

bool supported_image_extension(const fs::path& path) {
  const auto ext = lower_wide(path.extension().native());
  static const std::array extensions{L".jpg",  L".jpeg", L".png", L".webp",
                                    L".bmp",  L".tif",  L".tiff"};
  return std::ranges::find(extensions, ext) != extensions.end();
}

int natural_compare(std::wstring_view left, std::wstring_view right) {
  std::size_t i = 0;
  std::size_t j = 0;
  while (i < left.size() && j < right.size()) {
    if (std::iswdigit(left[i]) && std::iswdigit(right[j])) {
      auto i_end = i;
      auto j_end = j;
      while (i_end < left.size() && std::iswdigit(left[i_end])) {
        ++i_end;
      }
      while (j_end < right.size() && std::iswdigit(right[j_end])) {
        ++j_end;
      }
      auto l = std::wstring{left.substr(i, i_end - i)};
      auto r = std::wstring{right.substr(j, j_end - j)};
      l.erase(0, std::min(l.find_first_not_of(L'0'), l.size()));
      r.erase(0, std::min(r.find_first_not_of(L'0'), r.size()));
      if (l.size() != r.size()) {
        return l.size() < r.size() ? -1 : 1;
      }
      if (l != r) {
        return l < r ? -1 : 1;
      }
      i = i_end;
      j = j_end;
      continue;
    }
    const auto lc = std::towlower(left[i]);
    const auto rc = std::towlower(right[j]);
    if (lc != rc) {
      return lc < rc ? -1 : 1;
    }
    ++i;
    ++j;
  }
  if (i == left.size() && j == right.size()) {
    return 0;
  }
  return i == left.size() ? -1 : 1;
}

// 扫描输入目录时尽量使用 error_code，真正无法继续时再抛出明确异常。
std::vector<ImageFile> scan_images(const AppConfig& cfg) {
  std::error_code ec;
  if (!fs::exists(cfg.input_dir, ec) || ec) {
    throw std::runtime_error(
        std::format("输入文件夹不存在: {}", path_to_utf8(cfg.input_dir)));
  }
  if (!fs::is_directory(cfg.input_dir, ec) || ec) {
    throw std::runtime_error(
        std::format("输入路径不是文件夹: {}", path_to_utf8(cfg.input_dir)));
  }

  std::vector<fs::path> paths;
  for (fs::directory_iterator it{cfg.input_dir, ec}, end; it != end;
       it.increment(ec)) {
    if (ec) {
      throw std::runtime_error(
          std::format("扫描输入文件夹失败: {}", ec.message()));
    }

    const auto& entry = *it;
    if (entry.is_regular_file(ec) && !ec &&
        supported_image_extension(entry.path())) {
      paths.push_back(entry.path());
    }
  }

  std::ranges::sort(paths, [](const fs::path& a, const fs::path& b) {
    return natural_compare(a.filename().native(), b.filename().native()) < 0;
  });

  std::vector<ImageFile> files;
  files.reserve(paths.size());
  for (int i = 0; const auto& path : paths) {
    const auto size = fs::file_size(path, ec);
    if (ec) {
      throw std::runtime_error(
          std::format("读取文件大小失败: {}", path_to_utf8(path)));
    }
    files.push_back(
        {.path = path, .index = ++i, .size = size});
  }
  return files;
}

std::string format_size(std::uintmax_t bytes) {
  static constexpr std::array units{"B", "KB", "MB", "GB"};
  auto value = static_cast<double>(bytes);
  std::size_t unit = 0;
  while (value >= 1024.0 && unit + 1 < units.size()) {
    value /= 1024.0;
    ++unit;
  }
  return std::format("{:.2f} {}", value, units[unit]);
}

std::string format_seconds(double seconds) {
  if (seconds < 60.0) {
    return std::format("{:.1f}s", seconds);
  }
  return std::format("{:.1f}min", seconds / 60.0);
}

EncodeResult process_one(const ImageFile& image,
                         const AppConfig& cfg,
                         const fs::path& ffmpeg,
                         const fs::path& ffprobe,
                         const fs::path& temp_dir,
                         const ProcessRunner& runner,
                         FileLogger& logger) {
  const auto start = std::chrono::steady_clock::now();
  const auto output = cfg.output_dir / output_name_for(cfg, image);
  EncodeResult result{.index = image.index,
                      .input_path = image.path,
                      .output_path = output,
                      .original_size = image.size};

  // 每张图独立兜底：单文件异常会变成失败结果，不会终止整个批处理。
  try {
    if (fs::exists(output) && fs::file_size(output) > 0) {
      result.success = true;
      result.skipped = true;
      result.output_size = fs::file_size(output);
      return result;
    }

    const auto probe = probe_image(runner, ffprobe, image.path, logger);
    if (!probe) {
      result.error = "ffprobe 无法读取源图";
      return result;
    }

    auto pixel_format = pixel_format_for(cfg, *probe);
    result.pixel_format = pixel_format;
    const auto crf =
        cfg.use_crf_search && !cfg.lossless
            ? search_crf(runner, ffmpeg, image.path, temp_dir, cfg, *probe,
                         pixel_format, logger)
            : cfg.base_crf;
    result.crf = crf;

    auto attempt = encode_to_file(runner, ffmpeg, image.path, output, cfg,
                                  *probe, pixel_format, crf,
                                  cfg.final_cpu_used, false, logger);
    if (!attempt.ok && pixel_format != "yuv420p" &&
        pixel_format != "yuva420p") {
      pixel_format = probe->has_alpha ? "yuva420p" : "yuv420p";
      result.pixel_format = pixel_format;
      attempt = encode_to_file(runner, ffmpeg, image.path, output, cfg, *probe,
                               pixel_format, crf, cfg.final_cpu_used, false,
                               logger);
    }

    result.encode_seconds = attempt.seconds;
    result.command_line = attempt.command;
    if (!attempt.ok) {
      result.error = attempt.error.empty() ? "ffmpeg 编码失败" : attempt.error;
      return result;
    }

    result.success = true;
    result.output_size = fs::file_size(output);
    if (const auto ssim =
            measure_ssim(runner, ffmpeg, image.path, output, logger);
        ssim) {
      result.ssim = *ssim;
    }
  } catch (const std::exception& ex) {
    result.error = std::format("处理文件时发生异常: {}", ex.what());
    logger.error(result.error);
  } catch (...) {
    result.error = "处理文件时发生未知异常";
    logger.error(result.error);
  }
  result.total_seconds = std::chrono::duration<double>(
                             std::chrono::steady_clock::now() - start)
                             .count();
  return result;
}

std::string csv_escape(std::string value) {
  if (value.find_first_of(",\"\n\r") == std::string::npos) {
    return value;
  }
  auto wide = replace_all(wide_from_utf8(value), L"\"", L"\"\"");
  return '"' + utf8_from_wide(wide) + '"';
}

bool export_csv(const fs::path& output_dir,
                const std::vector<EncodeResult>& rows,
                std::string& error) {
  const auto csv_path = output_dir / L"avif_stats.csv";
  std::ofstream out(csv_path, std::ios::binary);
  if (!out) {
    error = std::format("无法写入 CSV: {}", path_to_utf8(csv_path));
    return false;
  }
  out << "\xEF\xBB\xBF";
  out << "index,input,output,original_size,output_size,compression_ratio,crf,"
         "ssim,encode_seconds,total_seconds,pixel_format,status,error,command\n";

  for (const auto& row : rows) {
    const auto ratio =
        row.original_size == 0
            ? 0.0
            : 1.0 - static_cast<double>(row.output_size) / row.original_size;
    const auto status =
        row.skipped ? "skipped" : (row.success ? "success" : "failed");
    out << row.index << ','
        << csv_escape(path_to_utf8(row.input_path.filename())) << ','
        << csv_escape(path_to_utf8(row.output_path.filename())) << ','
        << row.original_size << ',' << row.output_size << ','
        << std::format("{:.4f}", ratio) << ',' << row.crf << ','
        << (row.ssim >= 0.0 ? std::format("{:.5f}", row.ssim) : "") << ','
        << std::format("{:.3f}", row.encode_seconds) << ','
        << std::format("{:.3f}", row.total_seconds) << ','
        << csv_escape(row.pixel_format) << ',' << status << ','
        << csv_escape(row.error) << ',' << csv_escape(row.command_line) << '\n';
  }
  return true;
}

bool encoder_available(const ProcessRunner& runner,
                       const fs::path& ffmpeg,
                       std::string_view encoder) {
  const auto command = command_line_for(ffmpeg, {L"-hide_banner", L"-encoders"});
  const auto result = runner.run(command, std::chrono::seconds{30});
  return result.exit_code == 0 && result.output.find(encoder) != std::string::npos;
}

// 顶层流水线只返回退出码；可恢复错误优先显示友好信息。
export int run_pipeline(const AppConfig& cfg) {
  try {
    const auto ffmpeg = find_executable(L"ffmpeg.exe");
    const auto ffprobe = find_executable(L"ffprobe.exe");
    if (!ffmpeg || !ffprobe) {
      std::println("[FAIL] 未找到 ffmpeg/ffprobe，请确认 scoop 安装的 ffmpeg 在 PATH 中。");
      return 1;
    }

    std::error_code ec;
    fs::create_directories(cfg.output_dir, ec);
    if (ec) {
      std::println("[FAIL] 创建输出目录失败: {}", ec.message());
      return 1;
    }
    const auto temp_dir = cfg.output_dir / L"_tmp";
    fs::create_directories(temp_dir, ec);
    if (ec) {
      std::println("[FAIL] 创建临时目录失败: {}", ec.message());
      return 1;
    }

    FileLogger logger{cfg.output_dir};
    ProcessRunner runner;

    auto files = scan_images(cfg);
    if (files.empty()) {
      std::println("未找到图片。支持: jpg, jpeg, png, webp, bmp, tif, tiff");
      return 0;
    }

    if (!encoder_available(runner, *ffmpeg, cfg.encoder)) {
      std::println("[WARN] ffmpeg -encoders 未列出 {}，稍后仍会尝试编码。", cfg.encoder);
    }

    auto work_order = files;
    std::ranges::sort(work_order, [](const ImageFile& a, const ImageFile& b) {
      return a.size > b.size;
    });

    std::println("===== AVIF Console C++23 =====");
    std::println("输入: {}", path_to_utf8(cfg.input_dir));
    std::println("输出: {}", path_to_utf8(cfg.output_dir));
    std::println("文件: {}  并行: {}  编码器: {}  搜索: {}",
                 files.size(), cfg.max_jobs, cfg.encoder,
                 cfg.use_crf_search ? "on" : "off");

    std::vector<EncodeResult> results(files.size());
    std::atomic_size_t next{0};
    std::atomic_int done{0};
    std::mutex console_mutex;
    std::mutex result_mutex;
    const auto start = std::chrono::steady_clock::now();
    const auto workers =
        std::min<int>(cfg.max_jobs, static_cast<int>(files.size()));

    std::vector<std::jthread> threads;
    threads.reserve(static_cast<std::size_t>(workers));
    for (int worker = 0; worker < workers; ++worker) {
      // jthread 自动 join，避免忘记 join/detach 带来的退出风险。
      threads.emplace_back([&] {
        while (true) {
          const auto pos = next.fetch_add(1);
          if (pos >= work_order.size()) {
            break;
          }

          auto row = process_one(work_order[pos], cfg, *ffmpeg, *ffprobe,
                                 temp_dir, runner, logger);
          const auto current = ++done;
          {
            std::lock_guard lock{result_mutex};
            results[static_cast<std::size_t>(row.index - 1)] = row;
          }
          {
            std::lock_guard lock{console_mutex};
            const auto name = path_to_utf8(row.input_path.filename());
            if (row.skipped) {
              std::println("[{}/{}] SKIP {}", current, files.size(), name);
            } else if (row.success) {
              const auto ratio =
                  row.original_size == 0
                      ? 0.0
                      : 1.0 - static_cast<double>(row.output_size) /
                                  row.original_size;
              std::println(
                  "[{}/{}] OK {} -> {}  CRF={}  SSIM={}  压缩率={:.1f}%  {}",
                  current, files.size(), name,
                  path_to_utf8(row.output_path.filename()), row.crf,
                  row.ssim >= 0.0 ? std::format("{:.5f}", row.ssim) : "n/a",
                  ratio * 100.0, format_seconds(row.total_seconds));
            } else {
              std::println("[{}/{}] FAIL {}  {}", current, files.size(), name,
                           row.error);
            }
          }
        }
      });
    }
    threads.clear();

    const auto total_seconds =
        std::chrono::duration<double>(std::chrono::steady_clock::now() - start)
            .count();
    const auto success = std::ranges::count_if(results, [](const EncodeResult& row) {
      return row.success && !row.skipped;
    });
    const auto skipped = std::ranges::count_if(
        results, [](const EncodeResult& row) { return row.skipped; });
    const auto failed = std::ranges::count_if(
        results, [](const EncodeResult& row) { return !row.success; });
    const auto original_total = std::accumulate(
        results.begin(), results.end(), std::uintmax_t{},
        [](auto sum, const EncodeResult& row) {
          return row.success && !row.skipped ? sum + row.original_size : sum;
        });
    const auto output_total = std::accumulate(
        results.begin(), results.end(), std::uintmax_t{},
        [](auto sum, const EncodeResult& row) {
          return row.success && !row.skipped ? sum + row.output_size : sum;
        });
    const auto total_ratio =
        original_total == 0
            ? 0.0
            : 1.0 - static_cast<double>(output_total) / original_total;

    std::string csv_error;
    if (!export_csv(cfg.output_dir, results, csv_error)) {
      logger.error(csv_error);
      std::println("[WARN] {}", csv_error);
    }
    std::error_code ignored;
    fs::remove_all(temp_dir, ignored);

    std::println("\n================ 转换完成 ================");
    std::println("成功: {}  跳过: {}  失败: {}  总耗时: {}", success, skipped,
                 failed, format_seconds(total_seconds));
    std::println("原始: {}  输出: {}  总压缩率: {:.1f}%",
                 format_size(original_total), format_size(output_total),
                 total_ratio * 100.0);
    std::println("CSV: {}", path_to_utf8(cfg.output_dir / L"avif_stats.csv"));

    return failed == 0 ? 0 : 2;
  } catch (const std::exception& ex) {
    std::println("[FAIL] 流水线异常: {}", ex.what());
    return 1;
  } catch (...) {
    std::println("[FAIL] 流水线未知异常，程序已安全退出。");
    return 1;
  }
}

}  // namespace avif
