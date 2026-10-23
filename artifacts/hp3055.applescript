set options to {"Плоский сканер — A4, серый, 300 dpi", "Автоподатчик (ADF) — A4, серый, 300 dpi", "Плоский сканер — A4, цветной, 300 dpi"}
set choice to choose from list options with prompt "HP LaserJet 3055 — режим сканирования:" default items {item 1 of options}
if choice is false then return
set c to item 1 of choice
set flags to ""
if c contains "ADF" then
	set flags to " --adf"
else if c contains "цветной" then
	set flags to " --color"
end if
try
	do shell script quoted form of ("/Users/pyw0w/.hplip-scan/bin/hp3055-scan" & flags)
	display notification "Скан сохранён в ~/Documents/Scans" with title "HP LaserJet 3055"
on error errMsg
	display dialog "Ошибка сканирования:" & return & return & errMsg buttons {"OK"} default button "OK" with icon stop
end try
