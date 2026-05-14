#ifndef NOMINMAX
#define NOMINMAX
#endif

#include "avif_studio.h"

#include <algorithm>
#include <charconv>
#include <chrono>
#include <cctype>
#include <filesystem>
#include <format>
#include <iterator>
#include <memory>
#include <mutex>
#include <optional>
#include <ranges>
#include <string>
#include <string_view>
#include <system_error>
#include <thread>
#include <utility>
#include <vector>
#include <windows.h>
#include <shellapi.h>
#include <shobjidl.h>

import avif.config;
import avif.core;
import avif.pipeline;

namespace {

struct UiState {
  std::jthread worker{};
  slint::Timer theme_timer{};
  std::mutex mutex{};
};

std::string shared_to_string(const slint::SharedString& value) {
  return std::string{value.data(), value.size()};
}

std::string trim_copy(std::string value) {
  const auto is_space = [](unsigned char ch) { return std::isspace(ch) != 0; };
  const auto first = std::ranges::find_if_not(value, is_space);
  const auto last =
      std::ranges::find_if_not(value | std::views::reverse, is_space).base();
  if (first >= last) {
    return {};
  }
  return std::string{first, last};
}

int parse_int_or(std::string text, int fallback, int minimum, int maximum) {
  text = trim_copy(std::move(text));
  if (text.empty()) {
    return fallback;
  }
  int value{};
  const auto* begin = text.data();
  const auto* end = begin + text.size();
  const auto [ptr, ec] = std::from_chars(begin, end, value);
  if (ec != std::errc{} || ptr != end) {
    return fallback;
  }
  return std::clamp(value, minimum, maximum);
}

std::optional<int> parse_optional_int(std::string text, int minimum, int maximum) {
  text = trim_copy(std::move(text));
  if (text.empty()) {
    return std::nullopt;
  }
  return parse_int_or(std::move(text), minimum, minimum, maximum);
}

void append_token_defines(avif::AppConfig& cfg, std::string text) {
  std::size_t start = 0;
  while (start < text.size()) {
    const auto end = text.find_first_of(",;", start);
    auto token = trim_copy(text.substr(start, end - start));
    if (!token.empty()) {
      cfg.magick_defines.push_back(avif::wide_from_utf8(token));
    }
    if (end == std::string::npos) {
      break;
    }
    start = end + 1;
  }
}

slint::SharedString to_shared(std::string_view text) {
  return slint::SharedString{std::string{text}.c_str()};
}

bool windows_prefers_dark_mode() {
  // Windows stores the per-app theme choice in this user registry value.
  DWORD apps_use_light_theme = 1;
  DWORD value_size = sizeof(apps_use_light_theme);
  const auto status = RegGetValueW(
      HKEY_CURRENT_USER,
      L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
      L"AppsUseLightTheme", RRF_RT_REG_DWORD, nullptr, &apps_use_light_theme,
      &value_size);
  if (status != ERROR_SUCCESS) {
    return false;
  }
  return apps_use_light_theme == 0;
}

template <class Function>
void post_to_ui(slint::ComponentWeakHandle<AvifStudio> weak, Function&& fn) {
  slint::invoke_from_event_loop([weak, fn = std::forward<Function>(fn)]() mutable {
    if (auto app = weak.lock()) {
      fn(**app);
    }
  });
}

void append_task(AvifStudio& app, std::string_view line) {
  auto current = shared_to_string(app.get_task_list_text());
  if (!current.empty() && current.back() != '\n') {
    current.push_back('\n');
  }
  current += line;
  current.push_back('\n');

  constexpr std::size_t max_chars = 160000;
  if (current.size() > max_chars) {
    current.erase(0, current.size() - max_chars);
  }
  app.set_task_list_text(to_shared(current));
}

avif::CollisionMode collision_from_index(int index) {
  switch (index) {
    case 1:
      return avif::CollisionMode::skip;
    case 2:
      return avif::CollisionMode::suffix_time;
    case 3:
      return avif::CollisionMode::suffix_random;
    case 0:
    default:
      return avif::CollisionMode::overwrite;
  }
}

avif::AppConfig config_from_ui(const AvifStudio& app) {
  avif::AppConfig cfg;
  cfg.input_path = avif::wide_from_utf8(shared_to_string(app.get_input_path()));
  cfg.output_dir = avif::wide_from_utf8(shared_to_string(app.get_output_dir()));
  cfg.output_template =
      avif::wide_from_utf8(shared_to_string(app.get_template_text()));
  if (cfg.output_template.empty()) {
    cfg.output_template = L"{name}";
  }

  cfg.output_format =
      app.get_format_index() == 1 ? avif::OutputFormat::webp
                                  : avif::OutputFormat::avif;
  cfg.collision_mode = collision_from_index(app.get_collision_index());
  cfg.quality =
      parse_int_or(shared_to_string(app.get_quality_text()), 90, 1, 100);
  cfg.max_resolution =
      parse_int_or(shared_to_string(app.get_max_resolution_text()), 0, 0, 100000);
  cfg.max_jobs =
      parse_int_or(shared_to_string(app.get_threads_text()), 1, 1, 128);
  cfg.magick_speed =
      parse_optional_int(shared_to_string(app.get_speed_text()), 0, 8);
  cfg.strip_metadata = app.get_strip_metadata();
  cfg.write_summary = app.get_write_summary();
  cfg.write_log = app.get_write_log();
  append_token_defines(cfg, shared_to_string(app.get_defines_text()));
  return cfg;
}

std::optional<std::filesystem::path> choose_path(bool pick_folder) {
  const HRESULT init =
      CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
  const bool uninitialize = SUCCEEDED(init);
  if (FAILED(init) && init != RPC_E_CHANGED_MODE) {
    return std::nullopt;
  }

  IFileDialog* dialog = nullptr;
  HRESULT hr = CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER,
                                IID_PPV_ARGS(&dialog));
  if (FAILED(hr) || dialog == nullptr) {
    if (uninitialize) {
      CoUninitialize();
    }
    return std::nullopt;
  }

  DWORD options{};
  dialog->GetOptions(&options);
  options |= FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
  options |= pick_folder ? FOS_PICKFOLDERS : FOS_FILEMUSTEXIST;
  dialog->SetOptions(options);

  if (!pick_folder) {
    const COMDLG_FILTERSPEC filters[] = {
        {L"图片文件",
         L"*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff;*.gif;*.jxl;*.jp2;*.heic;*.heif;*.avif"},
        {L"所有文件", L"*.*"}};
    dialog->SetFileTypes(static_cast<UINT>(std::size(filters)), filters);
  }

  hr = dialog->Show(nullptr);
  if (FAILED(hr)) {
    dialog->Release();
    if (uninitialize) {
      CoUninitialize();
    }
    return std::nullopt;
  }

  IShellItem* item = nullptr;
  hr = dialog->GetResult(&item);
  dialog->Release();
  if (FAILED(hr) || item == nullptr) {
    if (uninitialize) {
      CoUninitialize();
    }
    return std::nullopt;
  }

  PWSTR raw_path = nullptr;
  hr = item->GetDisplayName(SIGDN_FILESYSPATH, &raw_path);
  item->Release();
  if (FAILED(hr) || raw_path == nullptr) {
    if (uninitialize) {
      CoUninitialize();
    }
    return std::nullopt;
  }

  std::filesystem::path result{raw_path};
  CoTaskMemFree(raw_path);
  if (uninitialize) {
    CoUninitialize();
  }
  return result;
}

std::filesystem::path effective_output_dir(const AvifStudio& app) {
  auto output = std::filesystem::path{
      avif::wide_from_utf8(shared_to_string(app.get_output_dir()))};
  if (!output.empty()) {
    return output;
  }
  const auto input =
      std::filesystem::path{avif::wide_from_utf8(shared_to_string(app.get_input_path()))};
  return avif::default_output_dir_for(input);
}

void open_path(std::filesystem::path path, bool create_if_missing) {
  if (path.empty()) {
    return;
  }

  std::error_code ec;
  if (std::filesystem::is_regular_file(path, ec) && !ec) {
    path = path.parent_path();
  }
  if (create_if_missing) {
    std::filesystem::create_directories(path, ec);
  }
  if (!std::filesystem::exists(path, ec)) {
    path = path.parent_path();
  }
  if (!path.empty()) {
    ShellExecuteW(nullptr, L"open", path.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
  }
}

}  // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
  auto app = AvifStudio::create();
  auto state = std::make_shared<UiState>();
  auto weak = slint::ComponentWeakHandle(app);

  const auto hardware_threads =
      static_cast<int>(std::max(1u, std::thread::hardware_concurrency()));
  app->set_threads_text(to_shared(std::to_string(hardware_threads)));
  app->set_system_dark_mode(windows_prefers_dark_mode());

  state->theme_timer.start(slint::TimerMode::Repeated, std::chrono::seconds{3}, [weak] {
    if (auto app = weak.lock()) {
      (*app)->set_system_dark_mode(windows_prefers_dark_mode());
    }
  });

  app->on_browse_input([weak] {
    if (auto app = weak.lock()) {
      const bool pick_folder = (*app)->get_input_mode_index() != 0;
      if (auto path = choose_path(pick_folder)) {
        const auto output = avif::default_output_dir_for(*path);
        post_to_ui(weak, [path = *path, output](AvifStudio& app) {
          app.set_input_path(to_shared(avif::path_to_utf8(path)));
          app.set_output_dir(to_shared(avif::path_to_utf8(output)));
        });
      }
    }
  });

  app->on_open_input([weak] {
    if (auto app = weak.lock()) {
      const auto input =
          std::filesystem::path{avif::wide_from_utf8(
              shared_to_string((*app)->get_input_path()))};
      open_path(input, false);
    }
  });

  app->on_browse_output([weak] {
    if (auto folder = choose_path(true)) {
      post_to_ui(weak, [folder = *folder](AvifStudio& app) {
        app.set_output_dir(to_shared(avif::path_to_utf8(folder)));
      });
    }
  });

  app->on_open_output([weak] {
    if (auto app = weak.lock()) {
      open_path(effective_output_dir(**app), true);
    }
  });

  app->on_cancel_conversion([state] {
    std::scoped_lock lock{state->mutex};
    if (state->worker.joinable()) {
      state->worker.request_stop();
    }
  });

  app->on_start_conversion([weak, state] {
    auto app = weak.lock();
    if (!app) {
      return;
    }

    avif::AppConfig cfg = config_from_ui(**app);
    (*app)->set_running(true);
    (*app)->set_progress(0.0f);
    (*app)->set_status_text(to_shared("准备中"));
    (*app)->set_task_list_text(to_shared(""));
    append_task(**app, "开始转换...");

    std::scoped_lock lock{state->mutex};
    if (state->worker.joinable()) {
      state->worker.request_stop();
      state->worker.join();
    }

    state->worker = std::jthread([weak, cfg = std::move(cfg)](std::stop_token token) {
      const auto summary = avif::run_batch(
          cfg,
          [weak](const avif::BatchProgress& event) {
            post_to_ui(weak, [event](AvifStudio& app) {
              if (event.total > 0) {
                const auto progress =
                    static_cast<float>(event.completed) /
                    static_cast<float>(event.total);
                app.set_progress(std::clamp(progress, 0.0f, 1.0f));
                app.set_status_text(to_shared(
                    std::format("{}/{}", event.completed, event.total)));
              }
              append_task(app, event.text);
            });
          },
          token);

      post_to_ui(weak, [summary](AvifStudio& app) {
        if (!summary) {
          append_task(app, std::format("[FAIL] {}", summary.error()));
          app.set_status_text(to_shared("失败"));
        } else if (summary->canceled) {
          app.set_status_text(to_shared("已取消"));
        } else {
          app.set_status_text(to_shared(summary->failed_count == 0 ? "完成" : "有失败"));
          app.set_progress(1.0f);
        }
        app.set_running(false);
      });
    });
  });

  app->run();
  {
    std::scoped_lock lock{state->mutex};
    if (state->worker.joinable()) {
      state->worker.request_stop();
      state->worker.join();
    }
  }
  return 0;
}
