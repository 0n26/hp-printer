# HP LaserJet 3055: сканирование на macOS 27 (Apple Silicon)

## Диагноз

HP LaserJet 3055 — это МФУ: принтер, сканер, копир и факс. Принтер находится в сети
по адресу **192.168.1.118** и отвечает на порты:

| Порт | Что это |
|------|---------|
| 80   | встроенный веб-сервер (http://192.168.1.118/) |
| 515  | LPD-печать |
| 9100 | RAW-печать (JetDirect) |
| 8290 | канал данных сканера (Dot4 Phoenix / PML) |
| 8291, 8292 | служебные каналы HP |

Почему сканирование «из коробки» не работает:

- **AirScan/eSCL не поддерживается** — запросы к `/eSCL/...` возвращают 404.
  Поэтому «Image Capture» и «Preview» сканер не видят: в macOS 27 для этой модели
  нет ICA-драйвера.
- **HP не выпускает драйвер сканера** для LaserJet 3050/3055 под современные macOS.
- **VueScan** для этой модели поддерживает только Windows (на странице модели:
  Mac — No, Network — No).
- **HPLIP умеет** сканировать LJ3055 по сети: в `models.dat` модель есть
  (`scan-type=2`, это PML, `plugin=0`), а нужные порты у принтера открыты.

Проверено на практике: сканы с принтера идут (A4, 150 и 300 dpi, серый/цвет).

## Что установлено

- Собран **HPLIP 3.26.4** (только нужные части: `libhpmud`, `libhpip`,
  `libsane-hpaio`) нативно под macOS 27 arm64. Docker/VM не используются.
- Библиотеки и данные моделей: `~/.hplip-scan/`
- SANE-бэкенд: `/opt/homebrew/lib/sane/libsane-hpaio.1.so` + строка `hpaio`
  в `/opt/homebrew/etc/sane.d/dll.conf`
- CLI: `/opt/homebrew/bin/hp3055-scan`
- Приложение: `~/Applications/HP LaserJet 3055 Scan.app`
- Сканы по умолчанию: `~/Documents/Scans/`

## Использование

### Приложение

Откройте **HP LaserJet 3055 Scan.app** (двойной клик). Появится выбор:

- плоский сканер, A4, серый, 300 dpi;
- автоподатчик (ADF), A4, серый, 300 dpi;
- плоский сканер, A4, цветной, 300 dpi.

Результат — PDF в `~/Documents/Scans`, автоматически открывается в Preview.

### Командная строка

```sh
hp3055-scan                 # A4, серый, 300 dpi → PDF
hp3055-scan --color         # цветной
hp3055-scan --dpi 600       # 600 dpi
hp3055-scan --adf           # многстраничный скан из автоподатчика
hp3055-scan --png           # PNG вместо PDF
hp3055-scan --out ~/Desktop/scan.pdf
hp3055-scan --no-open       # не открывать результат
hp3055-scan --ip 192.168.1.118
hp3055-scan --help
```

Для ADF многстраничный PDF собирается утилитой `pdfmerge` (PDFKit).

## Если IP принтера изменится

```sh
echo 'ip=192.168.1.200' > ~/.hplip-scan/printer.conf
```

Либо разово: `hp3055-scan --ip 192.168.1.200`.
Рекомендуется закрепить адрес за принтером в DHCP (резервирование).

## Диагностика

```sh
# принтер доступен?
nc -z -G 2 192.168.1.118 8290

# какие опции видит сканер?
scanimage -d 'hpaio:/net/HP_LaserJet_3055?ip=192.168.1.118' -A

# подробный лог
SANE_DEBUG_HPAIO=255 scanimage -d 'hpaio:/net/HP_LaserJet_3055?ip=192.168.1.118' \
  --mode Gray --resolution 75 --format=png -o /tmp/test.png
```

Если после `brew upgrade` перестало работать (Homebrew мог заменить `sane-backends`
и удалить бэкенд), просто выполните из этого каталога:

```sh
./install.sh
```

## Переустановка с нуля

```sh
cd ~/project/hp-printer
./install.sh
```

`install.sh` раскладывает заранее собранные бинарники из `./artifacts`, прописывает
конфиг, обновляет `dll.conf`, создаёт симлинк CLI и приложение.

## Исходники и патчи

В `patches/` лежат минимальные правки к HPLIP 3.26.4, без которых сборка под
macOS 27 / arm64 не проходит. Все патчи проверены `patch -p1 --dry-run`.

| Патч | Зачем |
|------|-------|
| `0001-configure-optional-avahi.patch` | убирает обязательную проверку avahi: для сканирования по IP он не нужен |
| `0002-hpmud-add-stdlib-include.patch` | `jd.c` использует `strtol`, но не включает `<stdlib.h>` |
| `0003-hpip-ulong-corefoundation-conflict.patch` | не переопределять `ULONG`, если подключён CoreFoundation |
| `0004-orblite-prototypes.patch` | прототипы `orblite_*`: современный clang не допускает неявные объявления |
| `0005-orblite-macos-types.patch` | убирает конфликтующий CoreFoundation include, добавляет `UInt16`/`UInt32` |
| `0006-orblite-dlfcn-dbg.patch` | `<dlfcn.h>` и заглушка `_DBG` для `orblite.c` |

Полная пересборка из исходников:

```sh
./build-from-source.sh
```

Скрипт скачивает HPLIP 3.26.4, применяет патчи, собирает `libhpmud`, `libhpip`
и `libsane-hpaio`, обновляет `./artifacts` и запускает `install.sh`.
Homebrew-пакеты `libusb`, `net-snmp`, `sane-backends` нужны и в работе;
`gawk` и `libtool` — только для сборки (скрипт поставит их сам).

## Удаление

```sh
rm -rf ~/.hplip-scan
rm -rf "$HOME/Applications/HP LaserJet 3055 Scan.app"
rm -f /opt/homebrew/bin/hp3055-scan
rm -f /opt/homebrew/lib/sane/libsane-hpaio.so /opt/homebrew/lib/sane/libsane-hpaio.1.so
# при желании уберите строку "hpaio" из /opt/homebrew/etc/sane.d/dll.conf
```

## Windows-версия

В папке `windows/` лежат две программы (.NET 8), которые говорят с принтером
напрямую по PML/SNMP и не требуют драйверов HP:

- консоль: `windows/Hp3055Scan/` (бинарник `publish/win-x64/Hp3055Scan.exe`);
- GUI: `windows/Hp3055ScanGUI/` (WinForms, собирается только на Windows).

Подробности, протокол и сборка — в [WINDOWS.md](WINDOWS.md).

## Ограничения

- PDF — это изображение без текстового слоя (OCR нет). При необходимости можно
  поставить `brew install tesseract` и распознавать отдельно.
- Image Capture / Preview не увидят сканер: они используют Apple ICA, а не SANE.
  Сканируйте через приложение или `hp3055-scan`.
- Настроено сканирование **по сети**. USB-подключение теоретически тоже возможно
  (HPLIP hpaio), но не настраивалось.
- Принтер старый (2006 г.), протокол проприетарный. Если прошивка/поведение
  принтера изменятся, первым делом смотрите отладочный вывод `SANE_DEBUG_HPAIO`.
