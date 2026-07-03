# Carrier DependenSee — Service & Connection Mapper

A Windows desktop tool that maps the services running on a machine and
continuously records inbound/outbound network connections over time, storing the
history locally for later querying and export. A Linux port is on the roadmap and
the codebase is already structured for it.

## What it does

- **Registered services** — snapshots every Windows service (name, state, start
  mode, owning PID, executable path, log-on account) via WMI.
- **Listening endpoints** — every process with an open TCP/UDP listening socket.
- **Active connections over time** — samples established TCP connections on a
  timer, attributing each to its owning process and classifying it as inbound or
  outbound, building a traffic history you can query and export.

## Architecture

The design separates a background **collector** (writer) from a **GUI reader**,
and isolates all OS-specific code behind interfaces so the Linux port is a
drop-in.

```
ServiceMap.sln
├─ src/
│  ├─ ServiceMap.Core                  net8.0        Models, SQLite storage (WAL), export, retention
│  ├─ ServiceMap.Platform.Abstractions net8.0        IServiceEnumerator, IConnectionSampler, IPlatformProvider
│  ├─ ServiceMap.Platform.Windows      net8.0-windows WMI + GetExtendedTcpTable/UdpTable P/Invoke
│  ├─ ServiceMap.Platform.Linux        net8.0        /proc/net + systemctl (port in progress)
│  ├─ ServiceMap.Engine                net8.0-windows Platform selection + collection orchestration
│  ├─ ServiceMap.Collector             net8.0-windows Windows Service (BackgroundService) writer
│  └─ ServiceMap.App                   net8.0        Avalonia reader GUI (cross-platform)
├─ tools/
│  └─ ServiceMap.LinuxProbe            net8.0        Headless validation of the Linux path
└─ scripts/
   ├─ install-service.ps1
   └─ uninstall-service.ps1
```

### Why these choices

- **Avalonia (not WPF)** for the GUI so the same code runs on Windows now and
  Linux later.
- **WMI** for services because, unlike `ServiceController`, it returns the PID,
  path, start mode, and account in one query.
- **`GetExtendedTcpTable`/`GetExtendedUdpTable` P/Invoke** because the managed
  `IPGlobalProperties` APIs do not expose the owning process id — the whole point
  of attribution.
- **SQLite with WAL journaling** so the collector can write while the GUI reads
  the same file concurrently.

## Data storage

- Default database: `C:\ProgramData\CarrierDependenSee\servicemap.db` (shared, machine-wide).
- Retention: connection samples older than `RetentionDays` (default 30) are pruned.
- Export: CSV and JSON, on demand from the History tab or on a schedule from the
  collector (`AutoExportEnabled`).

Collector settings live in `src/ServiceMap.Collector/appsettings.json`:

| Setting | Default | Meaning |
|---|---|---|
| `SamplingIntervalSeconds` | 5 | Connection sampling cadence |
| `ServiceScanIntervalSeconds` | 60 | Service snapshot cadence |
| `RetentionDays` | 30 | History window |
| `RetentionSweepMinutes` | 60 | How often pruning runs |
| `AutoExportEnabled` | false | Periodic CSV export |

## Build

Requires the **.NET 8 SDK** on the build machine only.

```powershell
dotnet build ServiceMap.sln -c Release
```

### Bundle the .NET runtime (no install needed on the target)

`scripts\publish.ps1` produces **self-contained, single-file** executables with
the .NET 8 runtime embedded, so the target machine needs no .NET installed:

```powershell
.\scripts\publish.ps1                 # win-x64 by default
.\scripts\publish.ps1 -Runtime win-arm64
```

Output lands in `dist\`:

```
dist\collector\CarrierDependenSee.Collector.exe   (~37 MB, runtime bundled)
dist\app\CarrierDependenSee.App.exe               (~46 MB, runtime bundled)
```

Each exe is a single file — the CLR, BCL, and all dependencies are packed inside.
Trimming is intentionally disabled because WMI and Avalonia use reflection, so the
size reflects the full runtime. To strip the `.pdb` debug symbols from a release
drop, add `-p:DebugType=none -p:DebugSymbols=false` to the publish commands.

## Installer (MSI)

For a one-click deployment, build a Windows Installer package. `installer\CarrierDependenSee.wxs`
is a WiX v5 definition that:

- installs the bundled collector and GUI under `Program Files\Carrier DependenSee`,
- registers the collector as the `CarrierDependenSeeCollector` Windows Service (LocalSystem,
  auto-start) and starts it during install,
- adds a Start Menu shortcut for the GUI and an Add/Remove Programs entry,
- stops and removes the service automatically on uninstall.

Build it on Windows (the .NET 8 SDK is the only prerequisite):

```powershell
.\installer\build-installer.ps1
# produces Carrier-DependenSee-1.1.0.0-x64.msi in the repo root
```

The script installs the WiX v5 dotnet tool if needed, publishes both executables
self-contained (runtime bundled), and compiles the `.msi`. Double-click the result
to install — Windows prompts for elevation, the service starts on its own, and the
viewer appears in the Start Menu.

> Note: WiX compiles MSIs on Windows only, so the `.msi` is produced there rather
> than as part of the cross-platform build.

## Install & run (Windows)

The collector needs administrator rights for full port-to-process attribution, so
it installs as a Windows Service running under LocalSystem. The GUI runs as a
normal user and reads the shared database.

1. From an **elevated** PowerShell prompt:

   ```powershell
   .\scripts\install-service.ps1
   ```

   This registers `CarrierDependenSeeCollector`, sets it to auto-start, and starts it.

2. Launch the GUI (`CarrierDependenSee.App.exe`). The **Dashboard** shows current services,
   listeners, and active connections, refreshing on a timer. **History** lets you
   filter stored samples by process, port, remote address, protocol, direction,
   and time window, and export the results. **Settings** lets you install / start /
   stop / uninstall the service (each prompts for UAC elevation) and change the
   database path and refresh interval.

3. To remove the service (elevated):

   ```powershell
   .\scripts\uninstall-service.ps1
   ```

You can also run the collector as a console app for debugging — just run
`CarrierDependenSee.Collector.exe` directly; it detects it is not under the SCM and logs
to the console.

## Direction inference

A TCP connection is classified as **inbound** when its local port is one the host
is also listening on in the same sweep, and **outbound** otherwise. Listening
sockets are recorded as **Listen**. UDP is connectionless, so only its listening
endpoints are captured.

## Linux roadmap

The Linux platform already parses `/proc/net/{tcp,tcp6,udp,udp6}` and enumerates
systemd units via `systemctl`; `tools/ServiceMap.LinuxProbe` runs it headless.
Remaining work to reach parity:

- Complete socket-inode → PID attribution (`/proc/[pid]/fd`) under root.
- Enrich systemd units with PID / exec path / enabled-state via `systemctl show`.
- Host the collector as a systemd daemon and wire `LinuxPlatformProvider` into
  `PlatformFactory`.
- Multi-target `ServiceMap.Engine` and `ServiceMap.App` to run the GUI on Linux.

## Notes & limitations

- Full attribution and complete service details require running the collector
  elevated (LocalSystem or an admin account).
- ETW-based per-flow byte counts and a node-graph topology view are planned for a
  future version; v1 presents the data as tables.
