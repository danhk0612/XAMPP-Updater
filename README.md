# XAMPP Updater

A Windows 11 .NET 8 WPF utility for safely updating **Apache, PHP, and MariaDB** inside an existing XAMPP installation, with optional **phpMyAdmin** management when a `phpMyAdmin` directory is present.

[한국어 README](README.ko-KR.md)

XAMPP Updater is designed to update individual components without reinstalling the entire XAMPP bundle. It detects the installation root and actual Windows services, prepares rollback backups, migrates configuration where appropriate, validates the changed runtime, and automatically restores the previous state when an update or rollback validation fails.

## Version 1.0 scope

Managed components:

- Apache
- PHP
- MariaDB
- phpMyAdmin, only when it exists inside the selected XAMPP root

Out of scope:

- Reinstalling or upgrading the complete XAMPP bundle
- Separately installed Apache/PHP/MariaDB/phpMyAdmin instances outside the selected XAMPP root
- Node.js, Perl, Tomcat, and other XAMPP components
- Arbitrarily repackaged installations that relocate the standard `apache`, `php`, `mysql`, or `phpMyAdmin` directories

See [Compatibility](docs/COMPATIBILITY.md) for the supported environment assumptions and regression matrix.

## Main features

- Automatic XAMPP root detection and manual path selection
- Current Apache, PHP, MariaDB, and phpMyAdmin version detection
- Windows service discovery by executable `ImagePath` instead of fixed service names
- MariaDB detection through both `mysqld.exe` and `mariadbd.exe`
- Upstream and XAMPP reference version discovery
- Latest and selectable major/minor release-series targets
- Local compatibility profiling:
  - PE architecture
  - PHP TS/NTS, compiler/API information
  - Apache PHP module integration
- Preflight checks before component replacement
- Rollback backups with manifest, size, and SHA256 validation
- Separate classification of normal rollback backups and rollback safety backups
- Rollback catalog filtering so only valid backups connected to the current installed version are offered
- Apache/PHP configuration migration and integration validation
- MariaDB logical + physical backup and upgrade workflow
- Service stop/start and post-change runtime validation
- Automatic restoration after failed updates or failed rollback validation
- Configuration snapshots before/after updates
- Configuration history, diff, integrity verification, selective restore, and safe entry-level merge
- Persistent operation logs and diagnostics ZIP export
- Korean / English UI with System, Korean, and English language modes
- Self-update from GitHub Releases with SHA256 verification and executable replacement
- win-x64 self-contained single-file executable

## Apache and PHP integration policy

Apache and PHP are managed independently, but XAMPP commonly loads PHP directly as an Apache module. Therefore, changing either component can affect whether Apache starts successfully.

XAMPP Updater does **not** automatically update or roll back the other component. Instead it:

1. Changes only the component selected by the user.
2. Reconciles Apache PHP SAPI references such as `LoadFile`, `LoadModule`, and `PHPIniDir` with the currently installed PHP when needed.
3. Validates PHP and Apache integration with checks including `php -v`, `php -m`, module DLL loading, `httpd -t`, and the Apache service state when it was originally running.
4. Restores only the component that was just changed if the resulting Apache/PHP combination cannot be validated.

This allows Apache and PHP rollback order to remain independent without silently changing the other component.

## Backup and rollback policy

Normal update backups are preserved as rollback points. Rollback-time safety backups are tracked separately and are not offered as normal rollback targets.

A rollback candidate is shown only when its metadata and files are valid and its target version corresponds to the currently installed component version. Manifest paths, file sizes, SHA256 values, and required MariaDB logical backups are validated before use.

Safety backups are automatically pruned using the current retention policy: older than 7 days or beyond the latest 3 per component. Existing legacy schema 1/2 backups remain supported.

See [Backup and Rollback Policy](docs/BACKUP_ROLLBACK_POLICY.md).

## phpMyAdmin

phpMyAdmin management appears only when the selected XAMPP root contains a `phpMyAdmin` directory.

The update flow includes:

- Installed version detection
- Official latest metadata lookup
- Official `all-languages.zip` download
- Official SHA256 verification
- PHP / database compatibility checks
- Complete rollback backup
- Preservation of `config.inc.php`, `.htaccess`, `upload`, and `save`
- Staging structure/version validation
- `config.inc.php` PHP syntax validation
- Folder replacement with automatic restoration on failure
- Rollback to a valid update-created phpMyAdmin backup

The phpMyAdmin update, rollback, browser login, and database query flow has been validated on a real XAMPP installation.

## Real-environment validation

The following update paths have been validated on Windows 11 XAMPP installations:

- Apache `2.4.41 → 2.4.68`
- PHP `7.3.11 → 8.5.10`
- PHP `8.2.12 → 8.5.10`
- MariaDB `10.4.8 → 10.4.34`
- MariaDB `10.4.34 → 10.6.28`
- MariaDB `10.6.28 → 12.3.3`
- Apache configuration snapshot restore
- phpMyAdmin update and rollback, followed by browser login and database query
- Full mixed-order sequence: Apache → PHP → MariaDB → phpMyAdmin update, then MariaDB → Apache → PHP → phpMyAdmin rollback
- Application self-update with executable replacement and restart

These results demonstrate the tested paths; they are not a guarantee for every custom XAMPP configuration.

## Language settings

The application supports:

- System default
- Korean
- English

The selected mode is stored in:

```text
%LOCALAPPDATA%\XAMPP-Updater\settings.json
```

Changing the language restarts the application automatically. Internal stage IDs remain stable English identifiers while user-facing UI and dialogs are localized.

## Diagnostics

The **Export diagnostics** action creates a ZIP containing operational information useful for troubleshooting.

Included:

- Application / OS / privilege / XAMPP detection information
- Current-session operation log
- Persistent operation logs
- Self-update log when present

Excluded:

- Raw configuration file contents
- Database contents
- Rollback backups
- Downloaded component packages
- Credentials

## Development build

Requirements:

- Windows 11
- .NET 8 SDK

```powershell
dotnet restore XamppUpdater.sln
dotnet build XamppUpdater.sln -c Release
```

Run from source:

```powershell
dotnet run --project .\src\XamppUpdater.App\XamppUpdater.App.csproj
```

## Publish

Create a win-x64 self-contained single-file build:

```powershell
dotnet publish .\src\XamppUpdater.App\XamppUpdater.App.csproj `
  -c Release `
  -p:PublishProfile=win-x64 `
  -o .\artifacts\win-x64
```

The primary output is `XAMPP-Updater.exe`.

GitHub Actions performs restore, build, smoke tests, self-contained publish, executable verification, and artifact upload. Release branches named `release/v*` additionally generate `XAMPP-Updater.exe.sha256` and publish both files to a GitHub Release.

## Documentation

- [Roadmap](docs/ROADMAP.md) — implementation history and completed milestones
- [Compatibility](docs/COMPATIBILITY.md) — supported XAMPP layouts and regression matrix
- [Backup and Rollback Policy](docs/BACKUP_ROLLBACK_POLICY.md) — rollback catalog and retention rules
- [Apache/PHP Integration](docs/APACHE_PHP_INTEGRATION.md) — integration validation policy
- [Deferred Hardening](docs/DEFERRED_HARDENING.md) — optional future ABI/signature/metadata hardening
- [Decisions](docs/DECISIONS.md) — project scope and technical decisions
