# RemapUSB

A Windows tray app that remaps the buttons of **specific USB devices** to new actions. It works with any USB device whose buttons act as keyboard keys or media keys: media remotes, extra keyboards and numpads, macro pads, presentation clickers, foot pedals that emulate a keyboard.

The remapping only applies to the devices you save: the same key coming from any other keyboard keeps working as usual. You can save several devices, each with its own buttons.

**Example, with a media remote:** Home opens an app instead of the browser, Back sends Esc, Menu becomes Play/Pause, another button closes an app.

> The app's interface is in Brazilian Portuguese.

## Install

1. Run `RemapUSB-Setup-<version>.exe` (see [Build the installer](#build-the-installer)).
2. The first time, Windows may show "Windows protected your PC", because the installer is not signed. Click **More info** and then **Run anyway**.

The installer:
- installs for the current user only, without asking for administrator rights;
- creates a Start menu shortcut;
- offers to **start RemapUSB with Windows**.

To update, run the new version's installer over the old one. Your settings are kept.

## Use

1. **Add a device:** click Adicionar dispositivo (Add device) and plug in the device within 30 seconds. If it is already plugged in, unplug it and plug it back. The first device connected in that window is the one saved.
2. **Record buttons:** click Gravar botões (Record buttons) and press each button on the device once. The list starts empty and each new button becomes a row. Click Concluir gravação (Finish recording).
3. **Choose the action:** click a row, choose the action and save. Button names can be edited right in the list.

**Closing the window** does not quit the app: it keeps running in the tray, as the remote icon next to the clock. From the icon you can open the window, pause remapping, toggle "start with Windows" and quit.

### Available actions

| Action | What it does |
|---|---|
| Keep original | the button keeps doing what it already did |
| Do nothing | the button does nothing |
| Send key or shortcut | sends the captured key or combination (e.g. Esc, Ctrl + Shift + T) |
| Media key | Play/Pause, next, previous, stop, volume, mute |
| Open app | opens the app, or brings its window to the front if it is already open |
| Close app | asks the app to close and, if it does not, terminates it |
| Open/close app | toggles: opens it if closed, closes it if open |
| Restart app | closes it (if open) and opens it again |
| Open website | opens the address in the default browser |
| Open file or folder | opens it with the default Windows program |
| Run command | runs the command without opening a window |

Apps can be Store/MSIX apps or regular `.exe` programs. Store apps are opened through their package, so they keep working after updates.

### Media buttons and keyboard buttons

A USB device shows up in Windows split into parts, and each part behaves differently:

| Part | Typical buttons | Original key |
|---|---|---|
| **Media** | Home, Back, volume, mute | **blocked**: only the new action happens |
| **Keyboard** | arrows, OK, Menu, Backspace, Delete | Windows does not allow blocking only the device's key, so you choose |

For keyboard buttons, the editor offers two options:

- **Let it through** (default): the new action happens and the original key also reaches the focused program.
- **Neutralize:** the key becomes an unused key (F13 to F24) on **every keyboard**, and the app gives the original key back to the other keyboards. This needs administrator rights and a restart, applied in **Configurações → Teclas neutralizadas no Windows** (Settings → Neutralized keys). While RemapUSB is closed, the key does nothing on any keyboard, so it is only worth it for rarely used keys such as Menu.

The uninstaller undoes neutralized keys and offers to restart. If the administrator prompt is declined, it explains how to undo them later.

### Limitations

- **Only keyboard and media buttons:** gamepads and joysticks, mouse buttons and buttons a vendor only exposes to its own software are not recognized.
- **Up to 12 neutralized keys at a time:** each neutralized key needs its own unused key (F13 to F24), so the app can tell them apart. The same key used by several buttons or devices counts once. The editor and Settings show how many are in use (x/12).
- **Power button:** not supported. Windows handles it before any program does.
- **"Air mouse" pointer:** ignored.
- **Bluetooth devices:** not recognized. The app identifies devices by their USB VID/PID.
- **Programs running as administrator:** Windows does not let a regular program send keys to them.

## Settings and log

- **Settings:** `%AppData%\RemapUSB\config.json`.
- **Log** (can be turned off in Settings): `%LocalAppData%\RemapUSB\logs`. When run from inside the repository, it goes to `app-<machine>-<date-time>.txt` at the root.
- The first log line shows the version, the commit and the build time. The same information appears in **Configurações → Sobre** (Settings → About).
- Keys from other keyboards are never logged.

## Development

Requires the .NET 10 SDK. WPF app (Windows Fluent theme), no external packages.

```bash
dotnet run --project src/RemapUSB.App
```

### Build the installer

With [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed, double-click **`installer\gerar-instalador.cmd`**. It publishes the app, compiles the installer and opens the `installer\Output` folder with `RemapUSB-Setup-<version>.exe`.

The two steps it runs, if you need to do them by hand:

1. **Publish:** `dotnet publish src/RemapUSB.App -p:PublishProfile=win-x64`. This produces a single `RemapUSB.exe` in `installer\publish` that runs without .NET installed.
2. **Compile:** open `installer\RemapUSB.iss` in Inno Setup → Build → Compile.

**Version:** `<base>.<commit count>`, e.g. `1.0.27`. The last number goes up on its own with every commit. The base (`RemapUsbBaseVersion` in `RemapUSB.App.csproj`) is set by hand: change it to mark a bigger release.

**Uninstall:** the uninstaller runs `RemapUSB.exe --desfazer-teclas`, which removes from the Scancode Map only the keys the app neutralized:

| Exit code | The uninstaller |
|---|---|
| 0, nothing to undo | uninstalls without asking for administrator rights |
| 10, undone | offers to restart at the end (the key only goes back to normal after a restart) |
| 1, administrator declined or error | warns that the keys are still swapped and how to undo them |

### How the engine works

The keyboard hook and Raw Input run on their own high-priority thread, separate from the interface.

- **Media buttons:** the hook holds the key Windows generates for the button and matches it with Raw Input, which tells which device it came from. The key Windows generates for each button is learned during recording.
- **Keyboard buttons:** blocking in the hook does not work, because once the key is blocked Windows does not even generate the Raw Input. That is why "let it through" and "neutralize" (Scancode Map) exist. The app keeps Scancode Map entries that are not its own.
- **RemapUSB window in focus:** in this case Windows sends the media button as an app command (`WM_APPCOMMAND`) instead of passing the key through the hook. The window discards the command for remapped buttons and the action still runs.

`[HOOK]` and `[APPCOMMAND]` lines in the log (the log itself is in Portuguese):

| Line | Meaning |
|---|---|
| `chegou ao hook com N ms de atraso` | Windows took more than 40 ms to call the hook |
| `segurada antes do Raw` | the key reached the hook before the device's Raw Input; the app waits up to 60 ms |
| `o Raw chegou, mas o hook não recebeu a tecla` | Windows did not pass the key through the hook: the action still runs, and the line says which window was in focus |
| `[APPCOMMAND] janela do RemapUSB recebeu ...` | with the app's window in focus, the button arrived as an app command; if the button is remapped, the command is discarded |

### Repository layout

| Folder | Contents |
|---|---|
| `src/RemapUSB.App` | the app |
| `installer` | Inno Setup script and `gerar-instalador.cmd` |
| `src/RemapUSB.Probe` | prototype 1: console that shows each button and which part of the remote it comes from |
| `src/RemapUSB.Proto2a` | prototype 2a: remapping with fixed mappings, which proved the technique the app uses |
| `tools` | `.reg` files prototype 2a used to neutralize Menu (the app does this in Settings) |
| `docs/mockup.html` | clickable mockup of the screens, approved before the app was built |

The prototypes are kept as a record of how the technique was validated. **Do not run Proto2a together with the app**, since both remap the same buttons.
