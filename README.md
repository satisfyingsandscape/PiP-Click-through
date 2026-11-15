# PiP Click-through

A lightweight Windows system-tray utility that makes Picture-in-Picture (PiP) windows in Opera, Chrome, and Firefox click-through, allowing mouse input to reach the window underneath it.

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
  - Find a supported browser PiP window
  - Restore mouse input
  - Start with Windows
  - Exit
- The application does not capture the video or modify Opera's video content.

When click-through is enabled, the application adds the `WS_EX_LAYERED` and `WS_EX_TRANSPARENT` extended window styles to the detected PiP window and asks Windows to keep it topmost.

## How to use

1. Open a video in Opera, Google Chrome, or Mozilla Firefox and detach it into Picture-in-Picture mode.
2. Run `PiPClickThrough.exe`.
3. Right-click the application icon in the Windows notification area and select **Find PiP window**.
4. Select **Enable click-through**.
5. Test it on the desktop or over a harmless window first. Then try it over your game in borderless-windowed mode.
6. To restore mouse input, press `Ctrl + Alt + P` or select **Restore mouse input** from the tray menu.

If the PiP window is not detected, do not force the application to target another window. Exit the application and report the PiP window title and your browser name and version.

## Important: experimental behavior

The behavior of Chromium/browser PiP windows can vary between browser and Windows versions. Automatic detection currently checks for a visible Opera or Opera GX popup window with video-like dimensions and a landscape aspect ratio. If a browser changes its PiP window structure, detection may need to be updated. Detection is based on the browser process, popup-window style, and video-like dimensions; unusual browser popups may still require refinement.

Adding `WS_EX_LAYERED` to a window owned by another process is not guaranteed to work identically on every Windows version or browser build. If the video disappears, flickers, or mouse clicks do not pass through, disable click-through from the tray menu or exit the application. The application attempts to restore the original window style when click-through is disabled or the application exits normally.

The global shortcut may be unavailable if another application is already using it.

## Build from source

The .NET SDK is **not required to run the downloaded executable**. The following tools are needed only if you want to compile the application yourself.

### Developer requirements

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

## Continuous integration and releases

GitHub Actions builds the self-contained Windows x64 executable on every push to the `main` branch and on version-tag pushes (`v*`). It creates a ZIP containing the executable and this README, uploads it as a workflow artifact, and publishes the ZIP to the GitHub Release configured by the workflow.

For the public download, use the repository's **Releases** page. The **Actions** artifact is a temporary build artifact retained for 30 days.

## Credits and contact

This project was developed with assistance from **ChatGPT (GPT-6)**, an AI model by OpenAI.

For questions or feedback, contact the project owner on Discord: **`@ozo_tls`**.

## License

No open-source license has been specified yet. All rights are reserved by default. If you want to reuse, modify, or redistribute this project, please contact the project owner first.
