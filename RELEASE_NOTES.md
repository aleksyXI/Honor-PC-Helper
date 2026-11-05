## Fork build (based on Wintego/Honor-PC-Helper 1.13.5)

Added, on top of everything upstream 1.13.5 does:

- **Display refresh rate** with a tray submenu ({max} Hz / 60 Hz / auto: maximum on AC, 60 on battery). Windows does
  not remember the rate, so it is restored after sleep, reboot and a power-source change. No administrator rights needed.
- **Power scheme follows the mode** (smart → Balanced, performance and HUNTER → High Performance), which removes a
  known PC Manager bug where the laptop stayed on High Performance in smart mode and heated up on battery.
- **Clamshell mode**: the lid no longer puts the laptop to sleep when an external monitor is connected
  (`LIDACTION` in the active power scheme), applied through the privileged task.
- **HONOR services are stopped** at sign-in and from a menu item: `OSDServer`, `MBAMainService`, `HnUpdateService`
  are stopped and disabled, processes from `C:\Program Files\HONOR` are ended.
- **A HONOR-style on-screen plate instead of system notifications**, with the original PC Manager icons, shown for
  the mode, backlight level, camera and microphone keys, plus firmware events upstream does not handle:
  touchpad off/on (`0x2A3`/`0x283`) and the HONOR app key (`0x28A`).
- **Autostart through a logon Task Scheduler task** with the highest privileges - no UAC prompt at sign-in.
  An `HKCU\…\Run` entry or Startup-folder shortcut left by older builds is migrated into the task and removed.

## Honor PC Helper 1.13.5

Fixed:

- After a self-update the new version now starts automatically instead of showing an error. Updating from 1.13.4 or older still uses the old updater, so if the error appears once more, just start Honor PC Helper again — the new version is already installed.
