# PiP Click-through

A lightweight Windows system-tray utility that makes Opera's Picture-in-Picture (PiP) window click-through, allowing mouse input to reach the window underneath it.

## Download and run

1. Open the repository's **Releases** page and download `PiPClickThrough-win-x64.zip` from the latest release. The ZIP is also available as a temporary artifact in the **Actions** tab.
2. Extract the ZIP file to a folder.
3. Run `PiPClickThrough.exe`.

**No .NET installation is required on the target PC.** The application is published as a self-contained, single-file Windows x64 executable. The .NET 8 runtime is bundled into the executable.

> This build targets 64-bit Windows 10/11. It is not a macOS or Linux application.

## Features

- Global keyboard shortcut: `Ctrl + Alt + P`
- System-tray menu:
  - Enable or disable click-through
  - Find the Opera PiP window
  - Restore mouse input
  - Start with Windows
  - Exit
- The application does not capture the video or modify Opera's video content.

When click-through is enabled, the application adds the `WS_EX_LAYERED` and `WS_EX_TRANSPARENT` extended window styles to the detected PiP window and asks Windows to keep it topmost.

## How to use

1. Open a video in Opera and detach it into Picture-in-Picture mode.
2. Run `PiPClickThrough.exe`.
3. Right-click the application icon in the Windows notification area and select **Find PiP window**.
4. Select **Enable click-through**.
5. Test it on the desktop or over a harmless window first. Then try it over your game in borderless-windowed mode.
6. To restore mouse input, press `Ctrl + Alt + P` or select **Restore mouse input** from the tray menu.

If the PiP window is not detected, do not force the application to target another window. Exit the application and report the PiP window title and your Opera version.

## Important: experimental behavior

The behavior of Chromium/Opera PiP windows can vary between browser and Windows versions. Automatic detection currently checks for a visible Opera or Opera GX popup window with video-like dimensions and a landscape aspect ratio. If Opera changes its PiP window structure, detection may need to be updated.

Adding `WS_EX_LAYERED` to a window owned by another process is not guaranteed to work identically on every Windows version or browser build. If the video disappears, flickers, or mouse clicks do not pass through, disable click-through from the tray menu or exit the application. The application attempts to restore the original window style when click-through is disabled or the application exits normally.

The global shortcut may be unavailable if another application is already using it.

## Build from source

### Requirements

- Windows 10/11
- .NET 8 SDK
- PowerShell

### Build

1. Open PowerShell in the project folder.
2. Run:

   ```powershell
   .\build.ps1
   ```

3. The script creates:
   - `publish\PiPClickThrough.exe`
   - `dist\PiPClickThrough-win-x64.zip`

The published executable is self-contained and single-file. The .NET 8 SDK is needed only to build the project, not to run the resulting application.

## Project structure

```text
.
├── .github/
│   └── workflows/
│       └── build.yml
├── PiPClickThrough/
│   ├── PiPClickThrough.csproj
│   └── Program.cs
├── build.ps1
└── README.md
```

## Continuous integration

GitHub Actions builds the self-contained Windows x64 executable on every push to the default branch and on manual workflow runs. The resulting portable ZIP is uploaded as a workflow artifact.

Open the repository's **Actions** tab, select the latest successful **Build portable Windows app** run, and download the `PiPClickThrough-win-x64` artifact.

## License

No license has been specified yet. All rights are reserved by default unless the repository owner adds a license.
