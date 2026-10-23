#!/bin/zsh
# Установка/восстановление нативного сканера для HP LaserJet 3055 на macOS.
# Использует заранее собранные HPLIP-библиотеки из ./artifacts.
set -euo pipefail

HERE="${0:A:h}"
ROOT="${HPLIP_SCAN_ROOT:-$HOME/.hplip-scan}"
SANE_LIBDIR="${SANE_LIBDIR:-/opt/homebrew/lib/sane}"
SANE_CONFDIR="${SANE_CONFDIR:-/opt/homebrew/etc/sane.d}"
BINDIR="${BINDIR:-/opt/homebrew/bin}"
DEFAULT_IP="${HP3055_IP:-192.168.1.118}"

if [[ ! -x /opt/homebrew/bin/scanimage ]]; then
  echo "Ошибка: не найден /opt/homebrew/bin/scanimage." >&2
  echo "Установите SANE: brew install sane-backends" >&2
  exit 1
fi

echo "==> Каталог: $ROOT"
mkdir -p "$ROOT/lib" "$ROOT/bin" "$ROOT/etc" "$ROOT/share/hplip/data"

cp "$HERE/artifacts/libhpmud.0.dylib" "$HERE/artifacts/libhpip.0.dylib" "$ROOT/lib/"
cp -R "$HERE/artifacts/models" "$ROOT/share/hplip/data/"
cp "$HERE/artifacts/hp3055-scan" "$HERE/artifacts/pdfmerge" "$ROOT/bin/"
chmod +x "$ROOT/bin/hp3055-scan" "$ROOT/bin/pdfmerge"
printf '[dirs]\nhome=%s/share/hplip\n' "$ROOT" > "$ROOT/etc/hplip.conf"

if [[ ! -f "$ROOT/printer.conf" ]]; then
  printf 'ip=%s\n' "$DEFAULT_IP" > "$ROOT/printer.conf"
fi

chmod u+w "$ROOT"/lib/*.dylib
install_name_tool -id "$ROOT/lib/libhpmud.0.dylib" "$ROOT/lib/libhpmud.0.dylib"
install_name_tool -id "$ROOT/lib/libhpip.0.dylib" "$ROOT/lib/libhpip.0.dylib"

echo "==> SANE backend: $SANE_LIBDIR"
mkdir -p "$SANE_LIBDIR"
cp "$HERE/artifacts/libsane-hpaio.1.so" "$SANE_LIBDIR/libsane-hpaio.1.so"
chmod u+w "$SANE_LIBDIR/libsane-hpaio.1.so"
ln -sf libsane-hpaio.1.so "$SANE_LIBDIR/libsane-hpaio.so"

for dep in libhpmud.0.dylib libhpip.0.dylib; do
  old="$(otool -L "$SANE_LIBDIR/libsane-hpaio.1.so" | awk -v d="/$dep" '$1 ~ d {print $1; exit}')"
  if [[ -n "$old" && "$old" != "$ROOT/lib/$dep" ]]; then
    install_name_tool -change "$old" "$ROOT/lib/$dep" "$SANE_LIBDIR/libsane-hpaio.1.so"
  fi
done

codesign -s - --force "$ROOT/lib/libhpmud.0.dylib" "$ROOT/lib/libhpip.0.dylib" \
  "$SANE_LIBDIR/libsane-hpaio.1.so" >/dev/null 2>&1 || true

if [[ -f "$SANE_CONFDIR/dll.conf" ]]; then
  grep -qx hpaio "$SANE_CONFDIR/dll.conf" || echo hpaio >> "$SANE_CONFDIR/dll.conf"
fi

ln -sf "$ROOT/bin/hp3055-scan" "$BINDIR/hp3055-scan"

echo "==> Приложение"
mkdir -p "$HOME/Applications"
osacompile -o "$HOME/Applications/HP LaserJet 3055 Scan.app" "$HERE/artifacts/hp3055.applescript" >/dev/null 2>&1 || true

echo
echo "Готово."
echo "  CLI:  hp3055-scan --help"
echo "  App:  ~/Applications/HP LaserJet 3055 Scan.app"
echo "  IP:   $ROOT/printer.conf (сейчас: $(sed -n 's/^ip=//p' "$ROOT/printer.conf" | head -1))"
echo "  Сканы: ~/Documents/Scans"
