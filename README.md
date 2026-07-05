# RobustDownloader

[中文](README.zh-CN.md)

RobustDownloader is a cross-platform desktop download manager built with .NET, Avalonia, and ShadUI. It focuses on resilient large-file downloads with queue management, resumable segmented downloading, site credentials, CRC64 workflows, and a clean operational UI.

## Authorship

This project's code was implemented entirely by AI. Human input was limited to product direction, design preferences, requirements, and review feedback.

## Features

- Multi-task download queue with start, stop, delete, start-all, and stop-all actions
- Resumable segmented downloads with configurable thread count and block size
- Global concurrency control
- Persisted task list scope: recent 50, recent 100, recent 200, or all tasks
- Automatic fallback to single-thread mode when the server does not support range requests
- Batch task creation by pasting one URL per line
- Custom HTTP headers per task and default headers in settings
- HTTP proxy settings: system proxy, no proxy, or manual proxy URL
- Site credential rules for Basic Auth
- Optional CRC64 extraction or skipping
- Optional file timestamp update from the server response
- Local task and settings persistence as JSON
- Dynamic language switching: Auto, English, Simplified Chinese, Traditional Chinese
- Theme selection: system, light, dark
- Collapsible task detail panel with logs, headers, diagnostics, and run mode
- Window title includes the application version from the project metadata

## Requirements

- .NET SDK compatible with the project target framework
- macOS, Windows, or Linux desktop environment supported by Avalonia

## Build And Run

```bash
dotnet restore src/RobustDownloader.sln
dotnet run --project src/RobustDownloader/RobustDownloader.csproj
```

To build only:

```bash
dotnet build src/RobustDownloader.sln
```

## Settings

Open **Settings** from the main toolbar to configure:

- Appearance: language and theme
- Download defaults: thread count, block size, concurrency, CRC64 behavior, timestamp behavior
- Network: HTTP proxy mode and manual proxy URL
- Save and headers: default save directory and default HTTP headers
- Credentials: site matching rules with username and password
- Data files: paths for task list JSON and settings JSON

## Project Structure

- `src/RobustDownloader.sln`: solution file
- `src/RobustDownloader/`: application source code, assets, and project file
- `README.md`, `README.zh-CN.md`, `LICENSE`: project documentation and license

## Data Files

RobustDownloader stores task and settings data under the user application data directory:

- `tasks.json`: saved task list
- `settings.json`: app settings

The exact paths are shown in **Settings > Data Files**.

## Notes

RobustDownloader keeps temporary download state where needed so stopped tasks can resume safely when possible. Server behavior still matters: some hosts do not support range requests or reliable resume semantics.

## License

MIT License. See [LICENSE](LICENSE).
