# HonorPCHelper — Доработки поверх Honor-PC-Helper

Все доработки для MagicBook Pro 14 (Intel Arrow Lake, Arc 130T) — полная замена PC Manager.
Хранение настроек: `HKCU\Software\HonorPCHelper` (единый стиль с оригиналом, PascalCase).

---

## 1. Режимы производительности и планы питания
- **Fn+P** (WMI 0x2A0/0x2A1): умный / производительный режим + OSD.
- **Планы питания (powrprof)**: умный → Balanced (`381b4222-…`), производительный → High Performance (`8c5e7fda-…`).
  Хук-фикс бага PC Manager: при переключении в умный режим план **принудительно** сбалансированный.
- **Синхронизация**: при старте, при событии режима, при смене питания.
- **Защита**: производительный недоступен на батарее (<20% / нет AC) →
  OSD «Сбой включения производительного режима» + причина (многострочный текст).
- **Сон**: при Suspend производительный отключается → сбалансированный план.
- Файлы: `PowerModeEventService.cs`, `PowerPlanHelper.cs`, `PerformanceModePolicy.cs`, `HelperApplicationContext.cs`.

## 2. Клавиши и железо (WMI-события OemWMIEvent)
| Клавиша | Код | Механизм |
|---|---|---|
| Камера | 0x288 | реестр `HKLM\SOFTWARE\HONOR\ASvidDMFT\Settings\CameraStatus` (0/1) — аппаратно через драйвер |
| Микрофон | 0x287 | CoreAudio `IAudioEndpointVolume::SetMute` (NAudio) + лампочка через `HardwareHal.SetMicLightStatus` |
| Тачпад | 0x2A3 (выкл) / 0x283 (вкл) | направленные события — только OSD (BIOS сам) |
| Скриншот | 0x28E | эмуляция PrintScreen (InputSimulator) |
| Вызов приложения | 0x28A | OSD imageIcon |

- Микрофон инициализируется из **реального состояния** (`MicControl.IsMicMuted()`).
- События из WMI-потока → `BeginInvoke` в UI (CoreAudio требует STA).
- Файлы: `PowerModeEventService.cs`, `CameraControl.cs`, `MicControl.cs`, `HelperApplicationContext.cs`.

## 3. Частота экрана (CCD)
- Подменю в трее: **{макс} Гц** / **60 Гц** + галочка на текущей реальной частоте (живая, меню пересобирается).
- **Авто: макс на сети / 60 от батареи** (чекбокс) — переключение при смене питания.
- **Сохранение**: `ScreenRefreshRate`, `ScreenRateAuto` (типизированные свойства HardwareSettings, миграция старых ключей).
- **Восстановление**: после сна (Resume), смены питания, при старте (`ScreenControl.Restore()`).
- OSD показывается **после инициализации экрана** (пауза 700 мс).
- Файлы: `ScreenControl.cs`, `Display/ScreenNative.cs`, `Display/DisplayNative.cs` (CCD из G-Helper), `HardwareSettings.cs`.

## 4. Clamshell (работа с закрытой крышкой)
- Пункт меню с галочкой + сохранение (`ClamshellMode`), применение при старте.
- **Включается только при подключённом внешнем мониторе**; иначе OSD «Невозможно включить clamshell-режим / Подключите внешний монитор».
- Механизм: `LIDACTION` = 0 (AC+DC) через powrprof; выкл → возврат к «сон» (1).
- Файлы: `ClamshellControl.cs`, `HelperApplicationContext.cs`.

## 5. OSD — в стиле родного (из LocalInterfaceConfig.ini)
- Окно **230×220** (прямоугольник), иконка **98×98**, скругление 14.
- **Два текста** (как NAME/DESCRI): заголовок 13px + описание 9px, обычный (не жирный) шрифт, плотная компоновка (текст сразу под иконкой).
- Тёмная полупрозрачная плашка (alpha 235), белый текст, без рамки.
- **Позиция**: по центру X, у нижнего края (отступ 96px — над панелью задач).
- **DPI-awareness** (per-monitor v2): все размеры масштабируются от DPI — чётко на любом масштабе.
- Иконки 240×240 (OnCamera/OffCamera, OnMic/EnMic, OnTouch/EnTouch, performance, HZChange…) EmbeddedResource + fallback эмодзи.
- Файлы: `OSDToast.cs`, `OSDNativeForm` (в `OSD.cs`), `OSDIcons/`.

## 6. Службы HONOR и автозапуск
- **При старте приложение всегда останавливает службы HONOR** (OSDServer, MBAMainService, HnUpdateService → Stopped+Disabled) и убивает процессы из `C:\Program Files\HONOR` (без OSD-подтверждения).
- Пункт меню «Остановить все службы HONOR» — то же вручную.
- **Автозапуск через задачу планировщика** «HonorPCHelper Startup» (ONLOGON, Highest — без UAC-промпта).
- Файлы: `HelperApplicationContext.cs` (StopHonorServices), `StartupManager.cs`.

## 7. Сборка и упаковка
- **Framework-dependent single-file** (~3.6 МБ), требует .NET Desktop Runtime (устанавливается отдельно).
- **Нативные DLL встроены в exe** (HardwareHal.dll, IPCMessage.dll, Utils.dll + msvcp/vcruntime как запас):
  извлекаются при старте в `%LOCALAPPDATA%\HonorPCHelper\native` и грузятся через `LoadLibrary`;
  системные VC-рантаймы не перезаписываются.
- Зависимости NuGet: NAudio (CoreAudio), System.Management, System.ServiceProcess.ServiceController.
- Комментарии из кода удалены; сборка без предупреждений.
- Файлы: `NativeLibraries.cs`, `HonorPCHelper.csproj`.

## 8. Прикладные фиксы по итогам реверса
- **Микрофон**: найден реальный механизм (дизассемблирование osdservice.exe) — CoreAudio SetMute, НЕ MMDevice-скрытие и НЕ реестр.
- **Камера**: драйвер ASvidDMFT глушит по реестру (не WMI).
- **Тачпад**: события направленные (2A3=off, 283=on) — toggle не нужен.
- **Кнопка питания / PC Manager**: службы HONOR не отвечают за кнопку (аппаратная, настроена в Windows по умолчанию).
- **Уроки железа**: перебор WMI-команд зарядного блока (0x1000–0x100F) может поставить EC в «стоп зарядки»
  → сброс только долгим удержанием кнопки питания (аппаратный сброс EC). В приложении лимит ставится только 0x1003.
