# Privacy

OpenGameMate v0.1.0 is designed to minimize retained data.

## What the application processes

- Only the explicitly selected Kenshi window, after the user starts companion mode
  and requests a screenshot, or explicitly enables optional periodic captures before
  Start. Periodic mode is off by default and the choice is not saved across launches.
  Supported intervals are 1, 2, 5 and 10 minutes, initially 2 minutes. Each occurrence
  additionally waits for a ready page and 10 seconds of stable ChatGPT output-audio
  silence. This does not monitor microphone audio or guarantee the user is not speaking.
- A single temporary PNG used for the current submission attempt.
- User-selected role text and fixed automatic/manual screenshot prompts.
- Local operational settings and privacy-safe diagnostic events.
- A dedicated WebView2 user-data directory managed by WebView2 to retain the user's sign-in and permissions.

## What the application does not read or collect

OpenGameMate does not read ChatGPT replies, conversation history, account details, cookies, login tokens, complete page HTML, microphone audio, system audio, game memory, or telemetry. It does not run an analytics service.

## Screenshots

Window capture can include private content rendered inside Kenshi. The application
requires explicit window selection and confirmation before capture. Selection is
not persisted. A missing, closed, changed, or minimized target fails without a
desktop fallback. The capture item remains bound to the selected window lifetime.
Process identity is checked through operating-system metadata; no game memory,
window titles, process paths, or credentials are read or logged. One known temporary
PNG is atomically replaced and removed after each attempt. A crash does not restore
the prior screenshot or running state. The reusable primary-display backend remains
in the library, but the application uses only the Kenshi-window capture service.

## Diagnostics

Logs contain allowlisted structured fields such as timestamp, application state, stable error code, image dimensions, file size, and exception type. They do not contain screenshots, prompt text, webpage content, account data, tokens, audio, or full user paths. A diagnostic ZIP is created only after the user chooses a destination.

## Browser profile

WebView2 stores sign-in and permission data in the application's dedicated profile. OpenGameMate does not inspect that data. To clear it, fully exit OpenGameMate, wait for WebView2 processes to stop, and manually delete the directory shown by the application. Recursive deletion is intentionally not performed by the client.
