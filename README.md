# Download Manager

A desktop download manager built with **C# and WPF**.

The project focuses on asynchronous programming, download management, HTTP Range requests, concurrent downloads, persistence, cancellation, retry logic, and MVVM architecture.

## Features

- Download files from HTTP/HTTPS URLs
- Real-time download progress
- Download speed and estimated remaining time
- Pause and resume downloads
- Resume interrupted downloads using HTTP Range requests
- Multiple simultaneous downloads
- Download queue with configurable concurrency
- Cancel active and queued downloads
- Retry failed downloads
- Persistent download history
- Restore downloads after application restart
- Continue partially downloaded files
- Automatic filename collision handling
- Delete downloads and downloaded files
- Download integrity checks
- Logging of download operations and errors
- SQLite database for persistent data

## Technologies

- **C#**
- **.NET**
- **WPF**
- **MVVM**
- **CommunityToolkit.Mvvm**
- **HttpClient**
- **Entity Framework Core**
- **SQLite**
- **Serilog**
- **async/await**
- **CancellationToken**
- **SemaphoreSlim**

## Architecture

The application is divided into several main layers:

```text
DownloadManager
├── Models
│   ├── DownloadItem
│   └── DownloadStatus
│
├── ViewModels
│   └── MainViewModel
│
├── Views
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
│
├── Services
│   ├── DownloadService
│   ├── DownloadProgress
│   └── DownloadRetryService
│
└── Data
    ├── DownloadRecord
    ├── DownloadDbContext
    └── DownloadRepository
```

### DownloadService

Responsible for the actual file downloading.

It uses `HttpClient` with `ResponseHeadersRead` and streams data directly to disk instead of loading the entire file into memory.

When a partially downloaded file is detected, the service sends an HTTP Range request and continues downloading from the existing byte offset.

The service also validates the final downloaded size when the expected file size is known.

### DownloadRetryService

Provides automatic retry logic for failed network requests.

A failed download can be attempted several times before being marked as failed.

### DownloadRepository

Handles persistence using Entity Framework Core and SQLite.

Each database operation uses its own `DbContext`, which prevents tracking conflicts and allows download operations to work independently.

### MainViewModel

Coordinates the application logic and connects the UI with the download services.

It manages:

- download lifecycle;
- queue;
- concurrent downloads;
- cancellation;
- pause/resume;
- retry;
- deletion;
- database persistence;
- restoration after application restart.

## Download Lifecycle

A download can move through the following states:

```text
Pending
   ↓
Queued
   ↓
Downloading
   ├──→ Paused
   │      ↓
   │   Downloading
   │
   ├──→ Retrying
   │      ↓
   │   Downloading
   │
   ├──→ Cancelled
   ├──→ Failed
   └──→ Completed
```

Queued downloads wait for an available download slot. When an active download finishes or is cancelled, the next queued download can start automatically.

## Persistence

Download information is stored in a SQLite database.

The database is created automatically in the user's local application data directory.

After restarting the application, unfinished downloads are restored and can be resumed.

For partially downloaded files, the actual file size on disk is used to determine the resume position.

## Logging

Application and download events are logged using **Serilog**.

Logs contain information about:

- application startup and shutdown;
- download start and completion;
- resumed downloads;
- retry attempts;
- network errors;
- download failures.

Log files are stored in the application's `Logs` directory.

## Requirements

- Windows
- .NET SDK / Runtime compatible with the project

## Running the Project

Clone the repository:

```bash
git clone <repository-url>
```

Open the solution in **Visual Studio** and build the project.

The SQLite database is created automatically on the first launch.

No additional database setup is required.

## Purpose

This project was created as a portfolio project to demonstrate practical experience with:

- WPF application development;
- MVVM architecture;
- asynchronous programming;
- HTTP networking;
- file streaming;
- cancellation and concurrency;
- download queue management;
- database persistence;
- error handling and retry mechanisms;
- application logging.

---

## License

This project is intended for educational and portfolio purposes.