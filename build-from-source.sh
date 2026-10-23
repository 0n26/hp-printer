#!/bin/zsh
# Сборка HPLIP 3.26.4 (libhpmud + libhpip + libsane-hpaio) под macOS arm64.
# Результат копируется в ./artifacts, затем вызывается ./install.sh.
#
# Требования (Homebrew): libusb net-snmp gawk libtool sane-backends
#                        + Xcode Command Line Tools
set -euo pipefail

BUILD_ONLY=0
if [[ "${1:-}" == "--build-only" ]]; then BUILD_ONLY=1; fi

HERE="${0:A:h}"
ROOT="${HPLIP_SCAN_ROOT:-$HOME/.hplip-scan}"
SANE_LIBDIR="${SANE_LIBDIR:-/opt/homebrew/lib/sane}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/hplip-build.XXXXXX")"
cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

echo "==> Проверка зависимостей"
for c in clang make curl tar patch xcrun codesign install_name_tool brew; do
  command -v "$c" >/dev/null || { echo "Не найден: $c" >&2; exit 1; }
done

for f in libtool gawk libusb net-snmp sane-backends; do
  brew list "$f" >/dev/null 2>&1 || brew install "$f"
done

TARBALL_URL="https://deb.debian.org/debian/pool/main/h/hplip/hplip_3.26.4+dfsg0.orig.tar.xz"
SRC="$WORK/src"

echo "==> Скачивание исходников"
curl -fL --retry 3 -o "$WORK/hplip.tar.xz" "$TARBALL_URL"
mkdir -p "$SRC"
tar -xf "$WORK/hplip.tar.xz" -C "$SRC"
cd "$SRC"/*

echo "==> Применение патчей"
for p in "$HERE"/patches/*.patch; do
  echo "    $(basename "$p")"
  patch -p1 < "$p"
done

BUILD="$WORK/build"
mkdir -p "$BUILD"
cd "$BUILD"

SDK="$(xcrun --show-sdk-path)"
export PKG_CONFIG_PATH="/opt/homebrew/opt/net-snmp/lib/pkgconfig:/opt/homebrew/opt/libusb/lib/pkgconfig"
export CPPFLAGS="-I$SRC/hplip-3.26.4+dfsg0 -I$SRC/hplip-3.26.4+dfsg0/common \
  -I$SRC/hplip-3.26.4+dfsg0/ip -I$SRC/hplip-3.26.4+dfsg0/io/hpmud \
  -I$SRC/hplip-3.26.4+dfsg0/scan/sane -I$SRC/hplip-3.26.4+dfsg0/protocol \
  -I/opt/homebrew/opt/net-snmp/include -I/opt/homebrew/opt/libusb/include \
  -I/opt/homebrew/opt/libusb/include/libusb-1.0 -I/opt/homebrew/opt/libjpeg-turbo/include \
  -I/opt/homebrew/include -I$SDK/usr/include \
  -include unistd.h -include sys/time.h -include string.h -include stdlib.h"
export LDFLAGS="-L/opt/homebrew/opt/net-snmp/lib -L/opt/homebrew/opt/libusb/lib \
  -L/opt/homebrew/opt/libjpeg-turbo/lib -L/opt/homebrew/lib"

echo "==> configure"
env CC=clang CXX=clang++ AWK=gawk \
  "$SRC/hplip-3.26.4+dfsg0/configure" --prefix="$BUILD/install" \
  --enable-lite-build --enable-network-build --enable-scan-build \
  --disable-gui-build --disable-fax-build --disable-doc-build --disable-dbus-build \
  --disable-qt4 --disable-qt5 --disable-policykit \
  --disable-hpcups-install --disable-hpps-install

# В lite-сборке Makefile тянет libhpdiscovery (avahi) и пустой libhpipp.
# Нам они не нужны: обнаружение идёт по IP, PML-скан работает без avahi.
echo "==> Правка Makefile (убираем avahi-discovery и пустой libhpipp)"
sed -i '' \
  -e 's/^am__append_1 = libhpdiscovery.la$/am__append_1 =/' \
  -e 's/^libhpmud_la_DEPENDENCIES = libhpdiscovery.la$/libhpmud_la_DEPENDENCIES =/' \
  -e 's/^libhpmud_la_LIBADD = libhpdiscovery.la$/libhpmud_la_LIBADD =/' \
  Makefile
python3 -I - "$BUILD/Makefile" <<'PY'
import re, sys
p = sys.argv[1]
s = open(p).read()
s = s.replace("libsane_hpaio_la_LIBADD = libhpip.la \\\n\tlibhpmud.la \\\n\tlibhpipp.la \\\n",
              "libsane_hpaio_la_LIBADD = libhpip.la \\\n\tlibhpmud.la \\\n")
s = s.replace("libsane_hpaio_la_DEPENDENCIES = libhpip.la \\\n\tlibhpmud.la \\\n\tlibhpipp.la",
              "libsane_hpaio_la_DEPENDENCIES = libhpip.la \\\n\tlibhpmud.la")
open(p, "w").write(s)
PY

echo "==> Сборка"
make -j"$(sysctl -n hw.ncpu)" hplip_confdir="$ROOT/etc" libhpmud.la libsane-hpaio.la

echo "==> Обновление ./artifacts"
cp .libs/libhpmud.0.dylib .libs/libhpip.0.dylib .libs/libsane-hpaio.1.so "$HERE/artifacts/"

if [[ "$BUILD_ONLY" == "1" ]]; then
  echo "==> Сборка завершена (--build-only, установка пропущена)"
else
  echo "==> Установка"
  "$HERE/install.sh"
fi

echo
echo "Сборка завершена. Проверка:"
echo "  hp3055-scan --dpi 150 --no-open --out /tmp/scan-test.pdf"
