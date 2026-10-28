# Backup and rollback catalog policy

## Goals

XAMPP Updater keeps two different kinds of component backups:

1. **Rollback backup** - created before a normal component update. This is a user-visible rollback point.
2. **Safety backup** - created immediately before a manual rollback. This is only an emergency recovery copy for the rollback operation and is not offered as another rollback button target.

The distinction prevents a rollback from creating a misleading reverse rollback entry in the normal catalog.

## Rollback candidate rules

A standard Apache, PHP, or MariaDB backup is shown as a rollback candidate only when all of the following are true:

- the manifest belongs to the currently selected XAMPP root;
- the component type matches;
- the manifest is a `Rollback` backup, not a `Safety` backup;
- the installed version equals the manifest `TargetVersion`;
- the backed-up `CurrentVersion` is older than the installed version;
- the manifest location agrees with its declared `BackupRoot`;
- every manifest file exists and has the expected size;
- SHA256 integrity verification succeeds;
- MariaDB also has its required logical backup.

The latest valid candidate is used by the current one-button rollback UI.

Old schema 1/2 manifests remain compatible. Since those manifests did not have a backup-kind field, the default value is treated as `Rollback`. Old rollback-safety backups are not candidates because their version direction is current-newer -> target-older.

## Integrity policy

The catalog performs a complete SHA256 verification the first time a candidate manifest is observed in the running application. The result is cached so the one-second UI refresh does not repeatedly hash an entire component backup.

The selected backup is verified again immediately before rollback execution. Therefore a backup that is damaged after catalog discovery is still blocked before any component files are replaced.

## Retention policy

Formal rollback backups are **never automatically deleted** by the retention service.

Rollback safety backups are temporary recovery material and use a conservative automatic retention policy:

- keep for up to **7 days**;
- keep at most the **3 newest safety backups per XAMPP root and component**;
- corrupt or unreadable manifests are not automatically deleted;
- the existing explicit local-storage cleanup command remains the user's way to remove all managed backup/package data.

Schema 1/2 safety backups can be recognized conservatively when their recorded version direction is newer -> older.

## Scope

This policy applies to the common Apache/PHP/MariaDB `BackupManifest` catalog. phpMyAdmin continues to use its dedicated folder-replacement rollback metadata and validation path; its rollback behavior is intentionally not forced through the binary-component manifest model.

## Apache DLLs modified by PHP updates

A PHP update may additionally synchronize dependencies of `php_curl.dll` into the existing Apache `bin` directory. The original files are backed up under `apache-php-dll-sync/` in the PHP rollback backup, with an `apache-php-dll-sync.json` journal containing original and applied SHA256 values. Integrity verification checks these snapshots before a rollback. Existing backups without the journal continue to work.

On PHP rollback, a DLL still matching the applied hash is restored to its original hash. A DLL modified independently by a subsequent Apache update is not overwritten and is reported as a warning. If the overall integration rollback fails, the temporary pre-rollback Apache DLL snapshot is restored alongside the previous PHP files and configuration. Apache version changes independently re-evaluate cURL dependency collisions against the installed PHP binaries.
