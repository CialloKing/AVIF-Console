cmake_minimum_required(VERSION 3.30)

project(AVIFConsoleCpp
    VERSION 0.3.0
    DESCRIPTION "AVIF batch converter with MagickWand and Slint"
    LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(AVIF_STATIC_SLINT "Link Slint statically so AVIFStudio does not need slint_cpp.dll" ON)
option(AVIF_STATIC_MSVC_RUNTIME "Use /MT or /MTd for smaller redistribution bundles when all dependencies allow it" OFF)

if(AVIF_STATIC_MSVC_RUNTIME)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
else()
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
endif()

foreach(config Debug Release RelWithDebInfo MinSizeRel)
    string(TOUPPER "${config}" config_upper)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${config_upper}
        "${CMAKE_CURRENT_SOURCE_DIR}/bin/x64/${config}")
endforeach()

set(VCPKG_ROOT "$ENV{VCPKG_ROOT}" CACHE PATH "vcpkg root directory")
if(NOT VCPKG_ROOT AND EXISTS "D:/Scoop/apps/vcpkg/current")
    set(VCPKG_ROOT "D:/Scoop/apps/vcpkg/current" CACHE PATH "vcpkg root directory" FORCE)
endif()
if(AVIF_STATIC_MSVC_RUNTIME)
    set(AVIF_DEFAULT_VCPKG_TRIPLET "x64-windows-static")
else()
    set(AVIF_DEFAULT_VCPKG_TRIPLET "x64-windows")
endif()
set(VCPKG_TRIPLET "${AVIF_DEFAULT_VCPKG_TRIPLET}" CACHE STRING "vcpkg target triplet")
if(VCPKG_ROOT)
    list(PREPEND CMAKE_PREFIX_PATH "${VCPKG_ROOT}/installed/${VCPKG_TRIPLET}")
endif()

foreach(package_dir_var IN ITEMS scn_DIR FastFloat_DIR)
    if(DEFINED ${package_dir_var})
        string(FIND "${${package_dir_var}}" "/${VCPKG_TRIPLET}/" package_triplet_position)
        if(package_triplet_position EQUAL -1)
            message(STATUS "Clearing stale cached ${package_dir_var}: ${${package_dir_var}}")
            unset(${package_dir_var} CACHE)
        endif()
    endif()
endforeach()

message(STATUS "AVIF Studio vcpkg triplet: ${VCPKG_TRIPLET}")
message(STATUS "AVIF Studio MSVC runtime: ${CMAKE_MSVC_RUNTIME_LIBRARY}")

find_package(scn CONFIG REQUIRED)

set(MAGICK_ROOT "" CACHE PATH "ImageMagick runtime root built by scripts/build-magick.ps1")
if(NOT MAGICK_ROOT)
    if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/third_party/imagemagick-runtime/x64/Release/include/MagickWand/MagickWand.h")
        set(MAGICK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/third_party/imagemagick-runtime/x64/Release"
            CACHE PATH "ImageMagick runtime root built by scripts/build-magick.ps1" FORCE)
    elseif(EXISTS "D:/Scoop/apps/imagemagick/current/include/MagickWand/MagickWand.h")
        set(MAGICK_ROOT "D:/Scoop/apps/imagemagick/current"
            CACHE PATH "Development fallback ImageMagick runtime root" FORCE)
        message(WARNING "MAGICK_ROOT was not provided; using Scoop ImageMagick for this local build. Run scripts/build-magick.ps1 for a self-built runtime.")
    endif()
endif()

if(NOT EXISTS "${MAGICK_ROOT}/include/MagickWand/MagickWand.h")
    message(FATAL_ERROR "ImageMagick headers not found. Run scripts/build-magick.ps1 or configure with -DMAGICK_ROOT=<runtime root>.")
endif()

find_library(MAGICKWAND_LIBRARY
    NAMES CORE_RL_MagickWand_ CORE_DB_MagickWand_
    PATHS "${MAGICK_ROOT}/lib" "${MAGICK_ROOT}"
    NO_DEFAULT_PATH
    REQUIRED)
find_library(MAGICKCORE_LIBRARY
    NAMES CORE_RL_MagickCore_ CORE_DB_MagickCore_
    PATHS "${MAGICK_ROOT}/lib" "${MAGICK_ROOT}"
    NO_DEFAULT_PATH
    REQUIRED)
file(GLOB MAGICK_ALL_LIBRARIES CONFIGURE_DEPENDS
    "${MAGICK_ROOT}/lib/*.lib"
    "${MAGICK_ROOT}/*.lib")
list(REMOVE_ITEM MAGICK_ALL_LIBRARIES "${MAGICKWAND_LIBRARY}" "${MAGICKCORE_LIBRARY}")

add_library(ImageMagickWandRuntime INTERFACE)
target_include_directories(ImageMagickWandRuntime INTERFACE "${MAGICK_ROOT}/include")
target_link_libraries(ImageMagickWandRuntime INTERFACE
    "${MAGICKWAND_LIBRARY}"
    "${MAGICKCORE_LIBRARY}"
    ${MAGICK_ALL_LIBRARIES}
    ws2_32
    bcrypt
    crypt32
    shlwapi)

function(avif_enable_common_options target)
    target_compile_features(${target} PUBLIC cxx_std_23)
    if(MSVC)
        target_compile_options(${target} PUBLIC /utf-8 /permissive- /EHsc)
        target_compile_definitions(${target} PUBLIC
            NOMINMAX
            WIN32_LEAN_AND_MEAN)
    endif()
endfunction()

function(avif_copy_magick_runtime target)
    file(GLOB magick_dlls CONFIGURE_DEPENDS "${MAGICK_ROOT}/*.dll")
    if(magick_dlls)
        add_custom_command(TARGET ${target} POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different
                    ${magick_dlls}
                    "$<TARGET_FILE_DIR:${target}>"
            VERBATIM)
    endif()

    foreach(config_file
            colors.xml configure.xml delegates.xml english.xml locale.xml log.xml
            mime.xml policy.xml thresholds.xml type.xml type-ghostscript.xml
            sRGB.icc License.txt NOTICE.txt)
        if(EXISTS "${MAGICK_ROOT}/${config_file}")
            add_custom_command(TARGET ${target} POST_BUILD
                COMMAND ${CMAKE_COMMAND} -E copy_if_different
                        "${MAGICK_ROOT}/${config_file}"
                        "$<TARGET_FILE_DIR:${target}>/${config_file}"
                VERBATIM)
        endif()
    endforeach()

    if(EXISTS "${MAGICK_ROOT}/modules")
        add_custom_command(TARGET ${target} POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_directory
                    "${MAGICK_ROOT}/modules"
                    "$<TARGET_FILE_DIR:${target}>/modules"
            VERBATIM)
    endif()
endfunction()

function(avif_copy_slint_runtime target)
    if(TARGET slint_cpp-shared)
        add_custom_command(TARGET ${target} POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different
                    "$<TARGET_FILE:slint_cpp-shared>"
                    "$<TARGET_FILE_DIR:${target}>"
            VERBATIM)
    elseif(AVIF_STATIC_SLINT)
        add_custom_command(TARGET ${target} POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E rm -f
                    "$<TARGET_FILE_DIR:${target}>/slint_cpp.dll"
            VERBATIM)
    endif()
    add_custom_command(TARGET ${target} POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E rm -f
                "$<TARGET_FILE_DIR:${target}>/slint-compiler.exe"
                "$<TARGET_FILE_DIR:${target}>/slint_compiler.pdb"
        VERBATIM)
endfunction()

include(FetchContent)
if(AVIF_STATIC_SLINT)
    set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
endif()
set(SLINT_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(SLINT_BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(SLINT_FEATURE_INTERPRETER OFF CACHE BOOL "" FORCE)
set(SLINT_FEATURE_RENDERER_SOFTWARE ON CACHE BOOL "" FORCE)
set(SLINT_FEATURE_RENDERER_FEMTOVG OFF CACHE BOOL "" FORCE)
FetchContent_Declare(Slint
    GIT_REPOSITORY https://github.com/slint-ui/slint.git
    GIT_TAG v1.16.1
    SOURCE_SUBDIR api/cpp)
FetchContent_MakeAvailable(Slint)

function(avif_add_slint_sources target slint_file)
    get_filename_component(slint_absolute "${slint_file}" REALPATH BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
    get_filename_component(slint_base "${slint_absolute}" NAME_WE)

    set(generated_header "${CMAKE_CURRENT_BINARY_DIR}/${slint_base}.h")
    set(generated_cpp "${CMAKE_CURRENT_BINARY_DIR}/slint_generated_${slint_base}_1.cpp")

    # Slint's depfile currently produces noisy MSBuild warnings when the repo path
    # contains spaces. The UI has a single .slint file, so a direct dependency is
    # enough and keeps Windows paths intact.
    add_custom_command(
        OUTPUT "${generated_header}" "${generated_cpp}"
        COMMAND "$<TARGET_FILE:Slint::slint-compiler>" "${slint_absolute}"
                -f cpp
                -o "${generated_header}"
                "--cpp-file=${generated_cpp}"
        DEPENDS Slint::slint-compiler "${slint_absolute}"
        COMMENT "Generating ${slint_base}.h"
        VERBATIM)

    target_sources(${target} PRIVATE "${generated_header}" "${generated_cpp}")
    target_include_directories(${target} PUBLIC "$<BUILD_INTERFACE:${CMAKE_CURRENT_BINARY_DIR}>")
endfunction()

add_library(avif_core STATIC)
avif_enable_common_options(avif_core)
target_link_libraries(avif_core PUBLIC
    scn::scn
    ImageMagickWandRuntime)
target_sources(avif_core
    PUBLIC
        FILE_SET avif_modules TYPE CXX_MODULES FILES
            src/app/config.ixx
            src/core/process.ixx
            src/backends/magick_backend.ixx
            src/app/pipeline.ixx)

add_executable(AVIFConsoleCli src/cli/main.cpp)
avif_enable_common_options(AVIFConsoleCli)
target_link_libraries(AVIFConsoleCli PRIVATE avif_core)
avif_copy_magick_runtime(AVIFConsoleCli)

add_executable(AVIFStudio WIN32 src/ui/main.cpp)
avif_enable_common_options(AVIFStudio)
target_link_libraries(AVIFStudio PRIVATE
    avif_core
    Slint::Slint
    ole32
    shell32
    uxtheme
    imm32)
avif_add_slint_sources(AVIFStudio ui/avif_studio.slint)
avif_copy_slint_runtime(AVIFStudio)
avif_copy_magick_runtime(AVIFStudio)
