#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <algorithm>
#include <exception>
#include <expected>
#include <functional>
#include <print>
#include <string>
#include <vector>
#include <windows.h>

import avif.config;
import avif.process;

template <class Value, class Function>
std::expected<Value, std::string> capture_expected(Function&& fn) noexcept {
  try {
    return std::invoke(std::forward<Function>(fn));
  } catch (const std::exception& ex) {
    return std::unexpected{std::string{ex.what()}};
  } catch (...) {
    return std::unexpected{std::string{"未知异常"}};
  }
}

int wmain(int argc, wchar_t* argv[]) {
  SetConsoleOutputCP(CP_UTF8);
  SetConsoleCP(CP_UTF8);

  try {
    std::vector<std::wstring> args;
    args.reserve(static_cast<std::size_t>(std::max(argc - 1, 0)));
    for (int i = 1; i < argc; ++i) {
      args.emplace_back(argv[i]);
    }

    if (args.empty()) {
      avif::print_help();
      return 0;
    }

    const auto parsed = avif::parse_arguments(args);
    if (!parsed) {
      std::println("[FAIL] {}", parsed.error());
      return 1;
    }
    if (parsed->should_exit) {
      return parsed->exit_code;
    }
    const auto exit_code =
        capture_expected<int>([&] { return avif::run_pipeline(parsed->config); });
    if (!exit_code) {
      std::println("[FAIL] {}", exit_code.error());
      return 1;
    }
    return *exit_code;
  } catch (const std::exception& ex) {
    std::println("[FAIL] {}", ex.what());
    return 1;
  } catch (...) {
    std::println("[FAIL] 未知异常，程序已安全退出。");
    return 1;
  }
}
