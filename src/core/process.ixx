module;

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cctype>
#include <cstdint>
#include <cwctype>
#include <filesystem>
#include <format>
#include <fstream>
#include <mutex>
#include <optional>
#include <ranges>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>
#include <windows.h>

export module avif.core;

import avif.config;

export namespace avif {

namespace fs = std::filesystem;

struct ProcessResult {
  int exit_code{-1};
  bool timed_out{false};
  std::string output{};
};

struct ImageFile {
  std::size_t index{};
  fs::path path{};
  std::uintmax_t bytes{};
};

struct EncodeResult {
  std::size_t index{};
  fs::path input_path{};
  fs::path output_path{};
  std::uintmax_t original_bytes{};
  std::uintmax_t output_bytes{};
  int quality{};
  int speed{};
  double seconds{};
  bool ok{false};
  bool skipped{false};
  std::string message{};
  std::string command{};
};

namespace core_detail {

class UniqueHandle {
 public:
  UniqueHandle() = default;
  explicit UniqueHandle(HANDLE value) : value_{value} {}
  UniqueHandle(const UniqueHandle&) = delete;
  UniqueHandle& operator=(const UniqueHandle&) = delete;
  UniqueHandle(UniqueHandle&& other) noexcept : value_{other.release()} {}
  UniqueHandle& operator=(UniqueHandle&& other) noexcept {
    if (this != &other) {
      reset(other.release());
    }
    return *this;
  }
  ~UniqueHandle() { reset(); }

  HANDLE get() const { return value_; }
  HANDLE release() {
    const auto old = value_;
    value_ = nullptr;
    return old;
  }
  void reset(HANDLE next = nullptr) {
    if (value_ != nullptr && value_ != INVALID_HANDLE_VALUE) {
      CloseHandle(value_);
    }
    value_ = next;
  }

 private:
  HANDLE value_{nullptr};
};

std::string narrow_ascii(std::wstring_view text) {
  std::string out;
  out.reserve(text.size());
  for (const wchar_t ch : text) {
    out.push_back(ch <= 0x7f ? static_cast<char>(ch) : '?');
  }
  return out;
}

bool has_path_separator(std::wstring_view text) {
  return text.find(L'\\') != std::wstring_view::npos ||
         text.find(L'/') != std::wstring_view::npos;
}

std::string trim_copy(std::string text) {
  const auto not_space = [](unsigned char ch) { return !std::isspace(ch); };
  const auto first = std::ranges::find_if(text, not_space);
  const auto last = std::ranges::find_if(text | std::views::reverse, not_space)
                        .base();
  if (first >= last) {
    return {};
  }
  return std::string{first, last};
}

std::string csv_escape(std::string value) {
  if (value.find_first_of(",\"\r\n") == std::string::npos) {
    return value;
  }
  std::string out{"\""};
  for (const char ch : value) {
    if (ch == '"') {
      out += "\"\"";
    } else {
      out.push_back(ch);
    }
  }
  out.push_back('"');
  return out;
}

void replace_all(std::wstring& text,
                 std::wstring_view token,
                 std::wstring_view value) {
  std::size_t pos = 0;
  while ((pos = text.find(token, pos)) != std::wstring::npos) {
    text.replace(pos, token.size(), value);
    pos += value.size();
  }
}

}  // namespace core_detail

std::string utf8_from_wide(std::wstring_view text) {
  if (text.empty()) {
    return {};
  }
  const int required =
      WideCharToMultiByte(CP_UTF8, 0, text.data(),
                          static_cast<int>(text.size()), nullptr, 0, nullptr,
                          nullptr);
  if (required <= 0) {
    return core_detail::narrow_ascii(text);
  }
  std::string out(static_cast<std::size_t>(required), '\0');
  WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                      out.data(), required, nullptr, nullptr);
  return out;
}

std::wstring wide_from_utf8(std::string_view text) {
  if (text.empty()) {
    return {};
  }
  const int required =
      MultiByteToWideChar(CP_UTF8, 0, text.data(),
                          static_cast<int>(text.size()), nullptr, 0);
  if (required <= 0) {
    std::wstring fallback;
    fallback.reserve(text.size());
    for (const char ch : text) {
      fallback.push_back(static_cast<unsigned char>(ch));
    }
    return fallback;
  }
  std::wstring out(static_cast<std::size_t>(required), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                      out.data(), required);
  return out;
}

std::string path_to_utf8(const fs::path& path) {
  return utf8_from_wide(path.native());
}

std::string win32_error_message(DWORD error) {
  wchar_t* buffer = nullptr;
  const DWORD flags = FORMAT_MESSAGE_ALLOCATE_BUFFER |
                      FORMAT_MESSAGE_FROM_SYSTEM |
                      FORMAT_MESSAGE_IGNORE_INSERTS;
  const DWORD length = FormatMessageW(flags, nullptr, error, 0,
                                      reinterpret_cast<wchar_t*>(&buffer), 0,
                                      nullptr);
  if (length == 0 || buffer == nullptr) {
    return std::format("Win32 error {}", error);
  }
  std::wstring message{buffer, buffer + length};
  LocalFree(buffer);
    return core_detail::trim_copy(utf8_from_wide(message));
}

std::wstring quote_argument(std::wstring_view value) {
  if (value.empty()) {
    return L"\"\"";
  }

  const bool needs_quote =
      value.find_first_of(L" \t\n\v\"") != std::wstring_view::npos;
  if (!needs_quote) {
    return std::wstring{value};
  }

  std::wstring out{L"\""};
  std::size_t backslashes = 0;
  for (const wchar_t ch : value) {
    if (ch == L'\\') {
      ++backslashes;
      continue;
    }
    if (ch == L'"') {
      out.append(backslashes * 2 + 1, L'\\');
      out.push_back(ch);
      backslashes = 0;
      continue;
    }
    out.append(backslashes, L'\\');
    backslashes = 0;
    out.push_back(ch);
  }
  out.append(backslashes * 2, L'\\');
  out.push_back(L'"');
  return out;
}

std::wstring command_line_for(const fs::path& executable,
                              std::span<const std::wstring> args) {
  std::wstring command = quote_argument(executable.native());
  for (const auto& arg : args) {
    command.push_back(L' ');
    command += quote_argument(arg);
  }
  return command;
}

std::optional<fs::path> find_executable(std::wstring_view executable) {
  const fs::path candidate{std::wstring{executable}};
  std::error_code ec;
  if (core_detail::has_path_separator(executable) || candidate.has_root_name()) {
    if (fs::exists(candidate, ec) && !ec) {
      return fs::absolute(candidate, ec);
    }
    return std::nullopt;
  }

  std::wstring buffer(MAX_PATH, L'\0');
  const DWORD length =
      SearchPathW(nullptr, std::wstring{executable}.c_str(), nullptr,
                  static_cast<DWORD>(buffer.size()), buffer.data(), nullptr);
  if (length == 0) {
    return std::nullopt;
  }
  if (length >= buffer.size()) {
    buffer.assign(length + 1, L'\0');
    SearchPathW(nullptr, std::wstring{executable}.c_str(), nullptr,
                static_cast<DWORD>(buffer.size()), buffer.data(), nullptr);
  }
  buffer.resize(wcslen(buffer.c_str()));
  return fs::path{buffer};
}

fs::path executable_directory() {
  std::wstring buffer(MAX_PATH, L'\0');
  DWORD length = GetModuleFileNameW(nullptr, buffer.data(),
                                    static_cast<DWORD>(buffer.size()));
  while (length == buffer.size()) {
    buffer.assign(buffer.size() * 2, L'\0');
    length = GetModuleFileNameW(nullptr, buffer.data(),
                                static_cast<DWORD>(buffer.size()));
  }
  buffer.resize(length);
  return fs::path{buffer}.parent_path();
}

class ProcessRunner {
 public:
  ProcessResult run(const fs::path& executable,
                    std::span<const std::wstring> args,
                    std::chrono::milliseconds timeout) const {
    return run(command_line_for(executable, args), timeout);
  }

  ProcessResult run(std::wstring command,
                    std::chrono::milliseconds timeout) const {
    SECURITY_ATTRIBUTES security{};
    security.nLength = sizeof(security);
    security.bInheritHandle = TRUE;

    HANDLE raw_read = nullptr;
    HANDLE raw_write = nullptr;
    if (!CreatePipe(&raw_read, &raw_write, &security, 0)) {
      return {.exit_code = -1,
              .timed_out = false,
              .output = win32_error_message(GetLastError())};
    }

    core_detail::UniqueHandle read_pipe{raw_read};
    core_detail::UniqueHandle write_pipe{raw_write};
    SetHandleInformation(read_pipe.get(), HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdOutput = write_pipe.get();
    startup.hStdError = write_pipe.get();
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);

    PROCESS_INFORMATION raw_process{};
    std::wstring mutable_command = std::move(command);
    const BOOL created =
        CreateProcessW(nullptr, mutable_command.data(), nullptr, nullptr, TRUE,
                       CREATE_NO_WINDOW, nullptr, nullptr, &startup,
                       &raw_process);
    write_pipe.reset();

    if (!created) {
      return {.exit_code = -1,
              .timed_out = false,
              .output = win32_error_message(GetLastError())};
    }

    core_detail::UniqueHandle process{raw_process.hProcess};
    core_detail::UniqueHandle thread{raw_process.hThread};

    std::string output;
    std::mutex output_mutex;
    std::jthread reader{[&] {
      std::array<char, 4096> buffer{};
      DWORD bytes_read = 0;
      while (ReadFile(read_pipe.get(), buffer.data(),
                      static_cast<DWORD>(buffer.size()), &bytes_read,
                      nullptr) &&
             bytes_read > 0) {
        std::scoped_lock lock{output_mutex};
        output.append(buffer.data(), buffer.data() + bytes_read);
      }
    }};

    const DWORD wait_ms =
        timeout.count() < 0
            ? INFINITE
            : static_cast<DWORD>(
                  std::min<std::int64_t>(timeout.count(), INFINITE - 1));
    const DWORD wait_result = WaitForSingleObject(process.get(), wait_ms);
    bool timed_out = false;
    if (wait_result == WAIT_TIMEOUT) {
      timed_out = true;
      TerminateProcess(process.get(), 124);
      WaitForSingleObject(process.get(), INFINITE);
    }

    DWORD exit_code = 0;
    GetExitCodeProcess(process.get(), &exit_code);
    read_pipe.reset();
    reader.join();

    std::scoped_lock lock{output_mutex};
    return {.exit_code = static_cast<int>(exit_code),
            .timed_out = timed_out,
            .output = output};
  }
};

class FileLogger {
 public:
  explicit FileLogger(fs::path output_dir)
      : log_dir_{std::move(output_dir) / L"log"},
        log_file_{log_dir_ / L"avif-console.log"} {
    fs::create_directories(log_dir_);
    info("===== NEW SESSION START =====");
  }

  void info(std::string_view message) { append("INFO", message); }
  void warn(std::string_view message) { append("WARN", message); }
  void error(std::string_view message) { append("ERROR", message); }

 private:
  void append(std::string_view level, std::string_view message) {
    std::scoped_lock lock{mutex_};
    std::ofstream stream{log_file_, std::ios::app | std::ios::binary};
    const auto now = std::chrono::floor<std::chrono::seconds>(
        std::chrono::system_clock::now());
    stream << std::format("[{:%F %T}] [{}] {}\n", now, level, message);
  }

  fs::path log_dir_;
  fs::path log_file_;
  std::mutex mutex_;
};

bool is_supported_image_extension(const fs::path& path) {
  auto ext = path.extension().wstring();
  std::ranges::transform(ext, ext.begin(),
                         [](wchar_t ch) { return std::towlower(ch); });
  return ext == L".jpg" || ext == L".jpeg" || ext == L".png" ||
         ext == L".webp" || ext == L".bmp" || ext == L".tif" ||
         ext == L".tiff" || ext == L".gif" || ext == L".jxl" ||
         ext == L".jp2" || ext == L".heic" || ext == L".heif";
}

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

  std::vector<ImageFile> files;
  for (fs::recursive_directory_iterator it{
           cfg.input_dir, fs::directory_options::skip_permission_denied, ec},
       end;
       it != end; it.increment(ec)) {
    if (ec) {
      ec.clear();
      continue;
    }
    if (!it->is_regular_file(ec) || ec) {
      ec.clear();
      continue;
    }
    if (!is_supported_image_extension(it->path())) {
      continue;
    }

    auto bytes = fs::file_size(it->path(), ec);
    if (ec) {
      bytes = 0;
      ec.clear();
    }
    files.push_back(
        ImageFile{.index = files.size(), .path = it->path(), .bytes = bytes});
    ec.clear();
  }

  std::ranges::sort(files, [](const ImageFile& left, const ImageFile& right) {
    return left.path.native() < right.path.native();
  });
  for (std::size_t i = 0; i < files.size(); ++i) {
    files[i].index = i;
  }
  return files;
}

std::wstring output_name_for(const AppConfig& cfg, const ImageFile& image) {
  std::wstring name = cfg.output_template;
  auto stem = image.path.stem().wstring();
  auto ext = image.path.extension().wstring();
  if (!ext.empty() && ext.front() == L'.') {
    ext.erase(ext.begin());
  }

  core_detail::replace_all(name, L"{index}", std::format(L"{:04}", image.index + 1));
  core_detail::replace_all(name, L"{name}", stem);
  core_detail::replace_all(name, L"{ext}", ext);
  if (fs::path{name}.extension().empty()) {
    name += L".avif";
  }
  return name;
}

std::string format_size(std::uintmax_t bytes) {
  constexpr double kib = 1024.0;
  constexpr double mib = kib * 1024.0;
  if (bytes >= static_cast<std::uintmax_t>(mib)) {
    return std::format("{:.2f} MiB", static_cast<double>(bytes) / mib);
  }
  if (bytes >= static_cast<std::uintmax_t>(kib)) {
    return std::format("{:.1f} KiB", static_cast<double>(bytes) / kib);
  }
  return std::format("{} B", bytes);
}

void write_csv(const fs::path& output_dir,
               std::span<const EncodeResult> results) {
  fs::create_directories(output_dir);
  std::ofstream csv{output_dir / L"summary.csv", std::ios::binary};
  csv << "\xEF\xBB\xBF";
  csv << "index,input,output,original_bytes,output_bytes,ratio,quality,speed,"
         "seconds,status,message,command\n";

  for (const auto& result : results) {
    const double ratio =
        result.original_bytes == 0
            ? 0.0
            : static_cast<double>(result.output_bytes) /
                  static_cast<double>(result.original_bytes);
    const char* status =
        result.ok ? (result.skipped ? "skipped" : "ok") : "failed";
    const std::string speed =
        result.speed < 0 ? "default" : std::to_string(result.speed);
    csv << (result.index + 1) << ','
        << core_detail::csv_escape(path_to_utf8(result.input_path)) << ','
        << core_detail::csv_escape(path_to_utf8(result.output_path)) << ','
        << result.original_bytes << ',' << result.output_bytes << ','
        << std::format("{:.4f}", ratio) << ',' << result.quality << ','
        << speed << ',' << std::format("{:.3f}", result.seconds) << ','
        << status << ',' << core_detail::csv_escape(result.message) << ','
        << core_detail::csv_escape(result.command) << '\n';
  }
}

}  // namespace avif
