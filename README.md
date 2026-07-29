# Blender Freecam for 3ds Max 2025

Blender Freecam navigates the active viewport without creating or moving a
scene camera. MAXScript reads and writes the viewport's affine transformation
matrix; a small .NET component supplies continuous Win32 keyboard and relative
mouse input.

## Install

Use the prebuilt package in `dist`:

1. Extract `BlenderFreecam_3dsMax2025.zip`.
2. In 3ds Max 2025, choose **Scripting > Run Script**.
3. Run `Install_BlenderFreecam.ms` from the extracted `BlenderFreecam` folder.
4. Open **Customize > Customize User Interface**.
5. In the **Blender Freecam** category, drag **Freecam** to a toolbar and/or
   assign it a keyboard shortcut.

The installer writes only to the current user's 3ds Max scripts and macros
directories. No administrator access is required.

If **Safe Scene Script Execution > Block 3rd Party .NET code** is enabled, 3ds
Max may reject `dotNet.loadAssembly`. Allow the installed local component before
using freecam.

## Controls

- Assigned shortcut or **Freecam** toolbar button: enter/leave freecam
- **W/A/S/D**: forward/left/back/right
- **Space** or **E**: move up on the world Z axis
- **Q**: move down on the world Z axis
- **Ctrl+Q / Ctrl+E**: roll left/right
- **Shift**: move four times faster
- Mouse: look
- Mouse wheel: adjust base movement speed
- **Ctrl + mouse wheel**: adjust viewport FOV (5–160 degrees)
- **Esc**: leave freecam

Movement uses measured elapsed time and is independent of redraw rate. Combined
movement is normalized, so diagonals are not faster.

Pitch is unrestricted: continuing to move the mouse vertically rotates through
the poles instead of stopping at a pitch limit. Mouse yaw, mouse pitch, and roll
all use the camera's current local axes.

## Viewport behavior and safety

- Perspective viewports are supported directly.
- A User viewport is promoted to Perspective while preserving its transform,
  because Autodesk's `viewport.setTM` only accepts Perspective views. It remains
  Perspective after navigation so the new fly position and orientation persist.
- Camera, light, top/bottom/front/back/left/right, grid, shape, and extended
  viewports are rejected.
- Navigation keys, mouse buttons, and mouse wheel events are intercepted while
  active, preventing normal 3ds Max shortcuts, selection, and viewport zoom.
- The pointer is hidden, confined to the captured viewport, and recentered for
  unlimited mouse-look.
- Leaving the mode restores the previous cursor position, cursor visibility,
  clip rectangle, and keyboard focus.
- Freecam stops automatically if 3ds Max loses foreground focus, the active
  viewport changes, the scene is reset/opened, or an update error occurs.
- Redraws are requested only when the view actually moves or rotates.
- Interactive redraw flags allow 3ds Max to reduce viewport quality temporarily
  in heavy scenes, and the update timer is capped near 60 Hz.
- A user Startup script reloads the controller and macro action automatically
  whenever 3ds Max starts.
- Input assemblies use versioned filenames, allowing updates without replacing
  a DLL that the current 3ds Max process has already loaded.

The viewport transform itself is intentionally not restored when freecam ends:
the purpose of the tool is to leave the viewport at the navigated position. No
scene objects or cameras are created or modified.

## Tunable values

Advanced users can adjust these fields in the MAXScript Listener:

```maxscript
BlenderFreecam.speed = 250.0
BlenderFreecam.fastMultiplier = 4.0
BlenderFreecam.mouseSensitivity = 0.15
BlenderFreecam.wheelMultiplier = 1.25
BlenderFreecam.fovStep = 2.0
BlenderFreecam.minFov = 5.0
BlenderFreecam.maxFov = 160.0
BlenderFreecam.rollSpeed = 90.0
```

`speed` is expressed in current scene units per second.

## Build from source

Requirements:

- Windows x64
- Visual Studio 2022 (or newer) with .NET Framework 4.8 targeting tools
- PowerShell 5.1 or newer

From PowerShell:

```powershell
.\build.ps1
```

The project has no NuGet or 3ds Max SDK dependency. Build output is placed in
`dist\BlenderFreecam`, with a ready-to-share ZIP beside it.

## Source layout

- `src/BlenderFreecam.Input`: .NET 4.8 Win32 input bridge
- `maxscript/BlenderFreecam_Core.ms`: viewport controller and timed update loop
- `maxscript/BlenderFreecam.mcr`: toolbar/hotkey macro action
- `Install_BlenderFreecam.ms`: per-user installer
