# Bundled ImageMagick Runtime

This directory contains a trimmed Windows runtime extracted from Scoop:

- Package: ImageMagick
- Version: 7.1.2-21 Q16-HDRI x64
- Source install path: `D:\Scoop\apps\imagemagick\current`

The application looks here first before falling back to `AVIF_MAGICK` or `PATH`.

Kept files:

- `magick.exe`
- runtime DLLs required by the Scoop build
- ImageMagick XML configuration files
- selected coder modules for common image formats and AVIF/HEIC
- `License.txt` and `NOTICE.txt`

Removed files:

- headers and import libraries
- demos, PerlMagick, uninstall files
- website documentation
- extra command aliases such as `compare.exe`, `mogrify.exe`, and `identify.exe`
