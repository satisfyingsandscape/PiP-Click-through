# PiP Click-through

A small Windows utility that lets mouse clicks pass through a browser's Picture-in-Picture (PiP) video window, so you can interact with the window or game underneath it.

## Download

1. Open [Releases](https://github.com/satisfyingsandscape/PiP-Click-through/releases).
2. Download `PiPClickThrough-win-x64.zip`.
3. Extract the ZIP and run `PiPClickThrough.exe`.

The download contains a **single standalone executable**. No separate runtime or installation is needed.

**Platform:** Windows 10/11 (64-bit). macOS and Linux are not supported.

## Supported browsers

- Opera and Opera GX
- Google Chrome
- Mozilla Firefox

PiP detection is experimental. The utility looks for a visible popup window owned by one of these browser processes and checks its dimensions and shape. Browser updates or unusual window layouts may affect detection, so support is not guaranteed on every browser version.

## Features

- Make the detected PiP window click-through.
- Keep the PiP window always on top.
- Toggle click-through with `Ctrl + Alt + P`.
- Control the app from the Windows notification-area (system tray) icon.
- Restore normal mouse input or exit from the tray menu.
- Optionally start the app with Windows.

The app does not capture, crop, or modify the video.

## How to use

1. Open a video in one of the supported browsers and switch it to Picture-in-Picture mode.
2. Run `PiPClickThrough.exe`.
3. Right-click its icon in the notification area and choose **Find PiP window**.
4. Choose **Enable click-through**.
5. Press `Ctrl + Alt + P` or choose **Restore mouse input** to turn click-through off again.

If the wrong window is detected, or the video flickers or disappears, disable click-through and report the browser and its version.

## Credits and contact

Developed with assistance from **ChatGPT (GPT-6)**, an AI model by OpenAI.

Discord: **@ozo_tls**

## License

No open-source license has been specified. All rights are reserved by default. Contact the project owner before reusing or redistributing the project.
