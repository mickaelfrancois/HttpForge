# HttpForge

A self-hosted HTTP client — a lightweight alternative to Insomnia and Postman. Built with Blazor Server on .NET 10, all data stored locally in a SQLite file.

## Features

- **HTTP requests** — GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- **Body types** — JSON, raw text, form-urlencoded
- **Variable system** — `{{varname}}` syntax with multi-level resolution: global base → global subset → collection base → collection subset → request
- **Environments** — global and per-collection variable sets, with subset switching (e.g. staging / production)
- **Collections & folders** — organise requests in a resizable sidebar
- **Post-request scripts** — JavaScript sandbox with access to response body, status, and variables
- **Insomnia import** — import Insomnia v5 collections and environment files (`.insomnia.rest/5.0`)
- **Dark / light / system theme**

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Run locally

```bash
git clone https://github.com/mickaelfrancois/HttpForge.git
cd HttpForge
dotnet run --project HttpForge
```

The app starts at `http://localhost:5000`. The database (`httpforge.db`) is created automatically on first run.

## Publish & run on Windows (no Docker)

For running on a Windows machine without Docker, publish a framework-dependent build and use the bundled launch scripts.

### Prerequisite on the target machine

- [ASP.NET Core 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the *Runtime*, not the SDK — the published build is framework-dependent)

### Publish

From the repo root:

```powershell
.\publish.ps1                       # publishes into .\dist
.\publish.ps1 -Output D:\Apps\HttpForge   # or to a custom folder
```

This produces the binaries plus `run.ps1` and `stop.ps1` in the output folder. Copy that folder to the target machine.

### Run

```powershell
cd .\dist        # or your chosen output folder
.\run.ps1
```

`run.ps1` starts HttpForge detached (the window can be closed), waits for it to respond, and opens `http://localhost:5000` in the browser. Running it again when the app is already up just reopens the browser.

The database, logs, and PID file live in `%LOCALAPPDATA%\HttpForge`, so they survive republishing over the same folder. To stop the app:

```powershell
.\stop.ps1
```

### Or just double-click `HttpForge.exe`

Double-clicking `HttpForge.exe` also works: it listens on `http://localhost:5000`, opens your browser automatically, and stores its database in `%LOCALAPPDATA%\HttpForge` (the same location as `run.ps1`). The difference is that a console window stays open — closing it stops the app. `run.ps1` is preferred when you want the app to keep running detached in the background.

## Docker Deployment

### Run with Docker Compose (pre-built image)

Create a `docker-compose.yml` on your server with the following content:

```yaml
services:
  httpforge:
    image: vladdfpc/httpforge:latest
    container_name: httpforge
    ports:
      - "8080:8080"
    environment:
      - HTTPFORGE_DATA=/data
    volumes:
      - httpforge-data:/data
    restart: unless-stopped

volumes:
  httpforge-data:
```

Then start the container:

```bash
docker compose up -d
```

The app is available at `http://localhost:8080`.

The `HTTPFORGE_DATA` environment variable controls where the database is stored inside the container. The named volume `httpforge-data` ensures the database persists across container restarts and updates.

### Update to a new version

```bash
docker compose pull
docker compose up -d
```

Your data is preserved in the named volume.

### Build and run from source

If you prefer to build the image yourself, replace the `image:` line with a `build:` directive:

```yaml
services:
  httpforge:
    build: .
    container_name: httpforge
    ports:
      - "8080:8080"
    environment:
      - HTTPFORGE_DATA=/data
    volumes:
      - httpforge-data:/data
    restart: unless-stopped

volumes:
  httpforge-data:
```

```bash
docker compose up -d --build
```

### Behind a reverse proxy

HttpForge listens on plain HTTP inside the container. TLS termination should be handled by your reverse proxy (nginx, Caddy, Traefik, etc.). Example with Caddy:

```
httpforge.example.com {
    reverse_proxy httpforge:8080
}
```

## Development

```bash
# Run with auto-reload
dotnet watch --project HttpForge

# Build
dotnet build HttpForge

# Run tests
dotnet test HttpForge.sln
```

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core 10 / Blazor Server |
| Database | SQLite via EF Core |
| Schema migrations | EF Core migrations (`db.Database.Migrate()` at startup) |
| Frontend | Bootstrap 5, CodeMirror 5 |
| Tests | xUnit, Moq |
