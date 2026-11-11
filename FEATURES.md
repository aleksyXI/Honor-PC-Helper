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

- Пункт меню с галочкой, хранение: `ClamshellMode` (намерение), `ClamshellEngaged` (режим записан в схему),
  `ClamshellDefaultLidAction` (что стояло до включения: 1 сон, 2 гибернация, 3 завершение).
- Механизм: `LIDACTION` = 0 пишется **только в ветку «от сети»**; на батарее ноутбук засыпает, иначе он остаётся
  работать в сумке. Снятие режима возвращает прежнее действие, а не жёстко «сон»; если режим не включало приложение,
  схема не трогается.
- Режим автоматический: держится, пока подключён внешний монитор **и** есть питание от сети. Пересматривается при
  запуске, при смене питания, после сна и по событию `SystemEvents.DisplaySettingsChanged` с паузой 2 с
  (смена конфигурации дисплея приходит пачкой). Условия нарушились - режим снимается сам.
- Включение из меню по-прежнему требует подключённого монитора, иначе плашка «Подключите внешний монитор».
- Применяется привилегированной задачей: `--apply-clamshell` (запасной путь `--set-clamshell` через UAC).
- Отличия от G-Helper, по которым схема и приведена: там тоже AC-only, тоже запоминается исходное действие и тоже
  есть реакция на смену дисплея; у нас раньше писались AC и DC сразу, действие не запоминалось, а условия
  проверялись только в момент включения.
- Файлы: `ClamshellControl.cs`, ветка `--apply-clamshell` в `PrivilegedHardware.Execute`,
  `ScheduleClamshellUpdate` / `OnClamshellTimerTick` в `HelperApplicationContext.cs`.

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
- Условия питания планировщика снимаются (`DisallowStartIfOnBatteries=false`, `StopIfGoingOnBatteries=false`),
  `ExecutionTimeLimit=PT0S` (без лимита: по умолчанию задача живёт 72 часа и прибивает приложение),
  `MultipleInstances=IgnoreNew`, `RestartCount=3` / `RestartInterval=PT1M` (перезапуск при падении).
- `SynchronizeAsync` при каждом старте сверяет зарегистрированную задачу с этим видом (путь, аргументы, условия
  питания, лимит, уровень запуска) и перерегистрирует её. Задачу с наивысшими правами может регистрировать только
  повышенный процесс, поэтому обычный экземпляр отдаёт работу привилегированной задаче (`--apply-startup-task`),
  а при её отсутствии - однократному запуску через UAC (`--set-startup-task`).
- Симптом, ради которого это сделано: задача, оставшаяся от прежней сборки, запускалась только от сети,
  завершалась при переходе на батарею и указывала на прежнюю папку - снаружи это «автозапуск не работает».
- Файлы: `StartupManager.cs`, ветки `--apply-startup-task` / `--set-startup-task` в `Program.cs` и
  `PrivilegedHardware.Execute`.

## 8. Датчик отпечатка при закрытой крышке

- Пункт меню «Отключать датчик отпечатка при закрытой крышке», хранение: `FingerprintOffLid`. Пункт показывается,
  только если в системе есть устройство класса `Biometric`.
- Состояние крышки приходит уведомлением `GUID_LIDSWITCH_STATE_CHANGE` (`BA3E0F4D-B817-4094-A2D1-D56379E6A0F3`,
  данные 0 - закрыта, 1 - открыта) в то же окно сообщений, где уже висит `GUID_CONSOLE_DISPLAY_STATE`.
- Устройство гасится и возвращается через `pnputil /disable-device` / `/enable-device` (+ `/scan-devices` после
  включения, иначе Windows Hello не видит датчик до перезагрузки) - только из привилегированной задачи:
  `--apply-fingerprint` (запасной путь `--set-fingerprint` через UAC). Состояние читается по
  `ConfigManagerErrorCode=22` (`CM_PROB_DISABLED`).
- Датчик возвращается в строй всегда, когда приложение не должно его держать выключенным: при открытии крышки,
  при выключении опции, при выходе из приложения (`Dispose`) и при завершении сеанса (`SessionEnding`), а также при
  каждом старте, если опция включена.
- Файлы: `FingerprintSensorControl.cs`, `OnLidChanged` / `SetFingerprintSensorAsync` в `HelperApplicationContext.cs`.

## 9. Сборка

- `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` (как у upstream),
  ~49 МБ, .NET Runtime в системе не нужен.
- `System.ServiceProcess.ServiceController` добавлен в зависимости (остановка служб HONOR).
- Сборка проверена в WSL: `dotnet build -c Release -p:EnableWindowsTargeting=true` — без предупреждений.
