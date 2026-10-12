# HonorPCHelper — доработки поверх Honor-PC-Helper

База: [Wintego/Honor-PC-Helper](https://github.com/Wintego/Honor-PC-Helper) **1.13.5** (self-contained single-file, ~49 МБ).
Ветка форка добавляет к оригиналу то, что есть в HONOR PC Manager, но чего в нём нет, — на MagicBook Pro 14
(Intel Arrow Lake, Arc 130T) этого хватает, чтобы PC Manager не был нужен вообще.

Настройки: `HKCU\Software\HonorPCHelper` (как в оригинале, PascalCase).

**Не дублируется** (уже есть в upstream): F7 (микрофон + лампочка), F8 (камера), F12 (снимок экрана),
виброотклик и краевые жесты тачпада, драйверы и BIOS, самообновление, тултип, режим HUNTER, планы питания не трогаются.

---

## 1. Частота экрана (CCD)

- Подменю **Экран**: `{макс} Гц` / `60 Гц`, галочка на текущей реальной частоте (меню пересобирается при каждом открытии).
- **Авто: максимум на сети / 60 от батареи** — переключение при смене питания.
- Хранение: `ScreenRefreshRate`, `ScreenRateAuto`.
- Восстановление: после сна (`OnDisplayResume`), при смене питания (`PowerStatus`), при запуске (`ScreenControl.Restore`).
- Плашка показывается после инициализации экрана (пауза 700 мс) — иначе на ней была бы прежняя частота.
- Прав администратора не требует: `EnumDisplaySettingsEx` + `ChangeDisplaySettingsEx`.
- Файлы: `ScreenControl.cs`, `Display/ScreenNative.cs`, `Display/DisplayNative.cs` (CCD).

## 2. Схема питания по режиму (powrprof)

- Умный режим → сбалансированная схема (`381b4222-…`), производительный и HUNTER → высокая (`8c5e7fda-…`).
- Точки вызова: запуск приложения, событие режима (Fn+P), выход из производительного режима.
- Снимает известный баг PC Manager: после возврата в умный режим схема остаётся высокой, ноутбук греется на батарее.
- `PowerSetActiveScheme` работает от имени пользователя — привилегированная задача не нужна.
- Файлы: `PowerPlanHelper.cs`.

## 3. Clamshell (работа с закрытой крышкой)

- Пункт меню с галочкой, хранение: `ClamshellMode`.
- Механизм: `LIDACTION` = 0 (AC и DC) в активной схеме, выключение возвращает «сон» (1).
- Включается только при подключённом внешнем мониторе, иначе плашка «Подключите внешний монитор».
- Применяется привилегированной задачей: `--apply-clamshell` (запасной путь `--set-clamshell` через UAC).
- Файлы: `ClamshellControl.cs`, ветка в `PrivilegedHardware.RunPendingCommand`.

## 4. Службы HONOR

- При входе в систему приложение само останавливает `OSDServer`, `MBAMainService`, `HnUpdateService`
  (`Stopped` + `Start=4`) и убивает процессы из `C:\Program Files\HONOR`.
- Пункт меню «Остановить службы HONOR» — то же вручную (если PC Manager запустился заново).
- Выполняется привилегированной задачей (`--apply-stop-services`) и молча: окно UAC фоном недопустимо.
- Файлы: `HonorServices.cs`.

## 5. Плашка OSD в стиле HONOR

- Своя слоёная плашка вместо системных всплывашек: 230×220, иконка 98, заголовок + описание, скругление 14,
  тёмная полупрозрачная (alpha 235), по центру X у нижнего края.
- DPI-aware (per-monitor v2): все размеры и шрифты считаются от DPI.
- Фирменные иконки PC Manager — `OSDIcons/*.png` (EmbeddedResource `HonorPCHelper.OSDIcons.*`), кэш, эмодзи-fallback.
- События: режим (`performance`/`intelligence`), подсветка (`OffLevel`/`LowLevel`/`HighLevel`), камера
  (`OnCamera`/`OffCamera`), микрофон (`OnMic`/`EnMic`), тачпад (`OnTouch`/`EnTouch`), клавиша приложения (`imageIcon`),
  частота экрана, отказ режима производительности и отказ clamshell.
- Показ только из потока интерфейса: событие WMI и фоновые задачи переходят в него через `_uiDispatcher`.
- Файлы: `OSDToast.cs` (плашка), `OSD.cs` (слоёное окно), `Program.ShowOsd`, `HelperApplicationContext.ShowNotification`.

## 6. События прошивки, добавленные к upstream

| Клавиша | Код | Что делает приложение |
| --- | --- | --- |
| Тачпад выключен / включён | `0x2A3` / `0x283` | только плашка (состояние меняет сама прошивка) |
| Вызов приложения HONOR | `0x28A` | плашка |

Коды F7/F8/F12/Fn+P/HUNTER и лампочка микрофона — из upstream, не дублируются.

## 7. Автозапуск задачей планировщика

- Задача `HonorPCHelper Startup`: триггер «при входе» (`UserId`), `LogonType=3`, `RunLevel=1` (Highest) —
  приложение поднимается с правами и **без запроса UAC**.
- Запись в `HKCU\…\Run` и ярлык из папки «Автозагрузка» прежних сборок переносятся в задачу и удаляются
  (`MigrateLegacyRunEntry`), чтобы приложение не запускалось дважды и не теряло права.
- Регистрация COM-задачей (`Schedule.Service`), путь перерегистрируется на текущий exe.
- Файлы: `StartupManager.cs`.

## 8. Сборка

- `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` (как у upstream),
  ~49 МБ, .NET Runtime в системе не нужен.
- `System.ServiceProcess.ServiceController` добавлен в зависимости (остановка служб HONOR).
- Сборка проверена в WSL: `dotnet build -c Release -p:EnableWindowsTargeting=true` — без предупреждений.
