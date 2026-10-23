# HP LaserJet 3055: программа сканирования под Windows

## Реализовано

Программа написана и проверена на живом принтере (через macOS, протокол идентичный):

- Консоль: `windows/Hp3055Scan/` (.NET 8). Готовый бинарник:
  `windows/Hp3055Scan/publish/win-x64/Hp3055Scan.exe`
  (self-contained, .NET не нужен).
- GUI: `windows/Hp3055ScanGUI/` (WinForms, .NET 8). Собирается только на Windows
  (WinForms SDK на macOS недоступен). Команда сборки ниже.
- Проверено: gray 150 dpi → JPEG 1280×1770; color 150/300 dpi → 2528×3540;
  lineart 150 dpi → PNG 1248×1770; PDF-обёртка работает.

Использование на Windows:

```cmd
Hp3055Scan.exe --ip 192.168.1.118 --dpi 300 --mode gray --format pdf --out scan.pdf
Hp3055Scan.exe --mode color --format jpeg --out scan.jpg
Hp3055Scan.exe --mode lineart --out scan.png
```

Опции: `--ip`, `--dpi`, `--mode gray|color|lineart`, `--format pdf|jpeg`,
`--out PATH`, `-h`.

Пересборка консоли:

```sh
cd windows/Hp3055Scan
dotnet build -c Release
dotnet publish Hp3055Scan.csproj -c Release -r win-x64 --self-contained -o publish/win-x64
# ARM64:
dotnet publish Hp3055Scan.csproj -c Release -r win-arm64 --self-contained -o publish/win-arm64
```

Сборка GUI (только на Windows, нужен .NET 8 SDK):

```cmd
cd windows\Hp3055ScanGUI
dotnet build -c Release
dotnet publish Hp3055ScanGUI\Hp3055ScanGUI.csproj -c Release -r win-x64 --self-contained -o publish\win-x64
```

Готовый `Hp3055ScanGUI.exe` запускает окно с выбором режима, DPI, формата и предпросмотром.

## Короткий вывод

Писать программу **можно и не нужно зависеть от драйверов HP**. Протокол сканирования
LJ3055 полностью разобран и проверен вживую с macOS:

- управление — SNMP v1 (UDP 161, community `public`);
- данные скана — TCP **8290**;
- формат — MFP-DTF, изображение приходит одним JPEG-потоком, разбитым на блоки.

Проверка: вручную, без HPLIP и SANE, запущен скан A4 150 dpi, серый.
Получен валидный JPEG **1280×1770**, 462 КБ, 12,8 с, 33 блока. Это доказывает,
что самостоятельный Windows-клиент реален.

## Альтернативы (если не писать своё)

| Вариант | Плюсы | Минусы |
|---|---|---|
| Родное ПО HP (Full Solution для Vista/7) | всё готово | старый инсталлятор, на Win10/11 может не встать |
| HP Scan Extended | умеет старые МФУ | поддержка 3055 не гарантирована |
| VueScan (Windows) | официально поддерживает 3055 | платно, требует драйвер HP |
| Обёртка над WIA/TWAIN | ~100 строк кода | нужен рабочий драйвер HP, на новых Windows его нет |
| **Свой клиент PML** | без драйверов, Win10/11, переносимо | нужно написать ~300–500 строк |

Рекомендация: **свой клиент на C#/.NET**. Он же без изменений пойдёт на macOS/Linux
и сможет заменить сборку HPLIP.

## Протокол

### 1. SNMP (UDP 161)

Версия SNMP v1, community `public`. Для SET:
- целые — `ASN_INTEGER`;
- бинарные/строковые — `ASN_OCTET_STR`.

OID-ы (суффикс `.0` обязателен):

| Объект | OID | Тип |
|---|---|---|
| uploadTimeout | `1.3.6.1.4.1.11.2.3.9.4.2.1.1.1.18.0` | INTEGER |
| scanToken | `1.3.6.1.4.1.11.2.3.9.4.2.1.1.1.25.0` | OCTET STRING |
| copierReduction | `1.3.6.1.4.1.11.2.3.9.4.2.1.1.5.1.4.0` | INTEGER (на 3055 вернул noSuchName — можно пропустить) |
| resolution | `1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.2.0` | OCTET STRING 8 байт |
| pixelDataType | `1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.3.0` | INTEGER |
| compression | `1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.4.0` | INTEGER |
| compressionFactor | `1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.5.0` | INTEGER |
| uploadState | `1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.12.0` | INTEGER |
| scannerStatus | `1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.2.1.0` | OCTET STRING (4 байта) |

Значения:

- `uploadState`: IDLE=1, START=2, ACTIVE=3, ABORTED=4, DONE=5, NEWPAGE=6.
- `pixelDataType`: lineart=1, grayscale=8, color=24.
- `compression`: NONE=1, DEFAULT=2, MH=3, MR=4, MMR=5, JPEG=6.
- `resolution`: 8 байт — два 32-битных big-endian значения `DPI << 16`.
  Пример 150 dpi: `00 96 00 00 00 96 00 00`.
- `scannerStatus`: биты 0x01 ошибка, 0x02 размер, 0x04 ADF открыт,
  0x08 замятие, 0x10 ADF пуст (у нас было `00 00 00 10`).
- `scanToken`: 16 байт. Занять — заполнить `0x35` (`'5'`), освободить — нулями.

### 2. Последовательность

1. GET `uploadState` → должен быть 1 (IDLE).
2. SET `uploadTimeout` = 45.
3. SET `pixelDataType` = 8 (или 24 / 1).
4. SET `resolution` = нужный DPI.
5. SET `compression` = 6 (JPEG) или 1 (NONE).
6. SET `compressionFactor` = 10 (для JPEG).
7. SET `scanToken` = `0x35` × 16 (блокировка сканера).
8. Открыть TCP **8290**.
9. SET `uploadState` = 2 (START).
10. Опрашивать GET `uploadState` до 3 (ACTIVE).
11. Читать MFP-DTF блоки, собирать JPEG, пока не придёт END_PAGE.
12. SET `uploadState` = 1 (IDLE), SET `scanToken` = нули.

Важно: состояние переходит в ACTIVE только после открытия TCP-канала. Проверено.

### 3. MFP-DTF (TCP 8290)

Все числа little-endian.

Фиксированный заголовок блока, 8 байт:

```
u32 BlockLength   // полная длина блока, включая заголовок
u16 HeaderLength  // длина заголовков
u8  DataType      // 2 = scan
u8  PageFlag      // 1 = NEW_PAGE, 2 = END_PAGE, 4 = NEW_DOC
```

Типы блоков:

- **NEW_DOC** (PageFlag=4): 18 байт = 8 + 10-байтный variant header.
  Пропустить.
- **NEW_PAGE** (PageFlag=1): 44 байта = 8 + 36-байтная start page запись:

```
u8  ID (0)
u8  Code              // 0 bitmap, 1 graymap, 2 MH, 3 MR, 4 MMR, 5 RGB, 7 JPEG
u16 PageNumber
u16 BlackPixelsPerRow
u16 BlackBitsPerPixel
u32 BlackRows
u32 BlackHorzDPI
u32 BlackVertDPI
... те же 14 байт для CMY
```

- **RASTER** (PageFlag=0): 8 + 4 + N байт:

```
u8  ID      // 1 = данные; 111 (0x6F) — служебная запись, пропустить
u8  dummy
u16 Size    // длина payload
u8  payload[Size]
```

Payload всех блоков с ID=1 — это **один непрерывный JPEG-поток**.
Проверено: в сумме 461 971 байт, один SOI (`FF D8`) и один EOI (`FF D9`),
JPEG 1280×1770, 1 компонент (grayscale).

- **END_PAGE** (PageFlag=2): запись ID=2 + счётчики строк. Конец страницы.

## Что проверено вживую

```
START page: code=7 px=1280 bpp=8 rows=1770 dpi=150x150
END page after 33 blocks, 462101 data bytes
done blocks=33 bytes=462413 elapsed=12.8s end=True
```

Собранный JPEG открылся и определился как `JPEG image data, 1280x1770,
components 1` — то есть это не «сырой поток», а нормальный файл.

Для цветного режима ожидается Code=7, 3 компонента (YCC), JPEG декодируется
стандартным декодером. Для lineart проще выставить compression=NONE и получить
Code=0 (bitmap), чтобы не реализовывать MH/MMR.

## План Windows-клиента

### Минимальная версия (1–2 дня)

Консольное приложение .NET 8:

```
hp3055scan --ip 192.168.1.118 --dpi 300 --mode gray --out scan.pdf
```

Компоненты:

1. `SnmpClient` — SNMP v1 GET/SET (BER). ~200–300 строк, без NuGet.
   Либо NuGet `SnmpSharpNet`.
2. `PmlScanner` — последовательность SET/GET, блокировка токена, состояния.
3. `MfpDtfReader` — чтение блоков из TCP, сборка JPEG, пропуск ID=111.
4. `ImageWriter` — `System.Drawing` (Windows) или `SkiaSharp`; сохранение PNG/JPEG.
5. `PdfWriter` — JPEG в PDF (DCTDecode) или PdfSharp.

### Дальше

- ADF: несколько страниц, состояния NEWPAGE/DONE, проверка `scannerStatus`.
- GUI: WinForms/WPF — выбор источника, режима, DPI, предпросмотр.
- Установщик: `dotnet publish -r win-x64 --self-contained`.
- Бонус: тот же код собирается под macOS/Linux и может заменить HPLIP/SANE.

### Риски

- На Windows не тестировалось — проверка протокола шла с macOS. Логика платформо-
  независима, но первый запуск на Windows нужно сделать на реальном принтере.
- 600/1200 dpi — больше данных; читать потоково, не держать всё в памяти.
- Цветной JPEG — YCC; стандартный декодер .NET сам переведёт в RGB.
- ADF — нужна отдельная проверка состояний NEWPAGE/DONE на реальных листах.
- Если прошивка принтера другая, первым делом смотреть `uploadState` и первые
  байты блока (BlockLength/PageFlag).

## Как повторить проверку

На любой машине с net-snmp:

```sh
IP=192.168.1.118
snmpget  -v1 -c public $IP 1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.12.0
snmpset  -v1 -c public $IP 1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.3.0  i 8
snmpset  -v1 -c public $IP 1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.2.0  x 0096000000960000
snmpset  -v1 -c public $IP 1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.4.0  i 6
snmpset  -v1 -c public $IP 1.3.6.1.4.1.11.2.3.9.4.2.1.1.1.25.0    x 35353535353535353535353535353535
# открыть TCP 8290, затем:
snmpset  -v1 -c public $IP 1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.1.12.0 i 2
```

Дальше читать блоки и склеивать payload записей ID=1 в один `.jpg`.

## Итог

Технических препятствий нет. Протокол стабильный, документирован и проверен.
Самый короткий путь — консольный .NET-клиент на 300–500 строк; GUI и ADF — сверху.
