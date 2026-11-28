fix: isolate database backups from upload hard stops
- Prevent automatic backups during `Upload HardStop` to avoid unnecessary flood waits.
- Implement `BeginBackgroundRpc` for backup operations to separate their flow control from general file uploads.
- Fix `FLOOD_WAIT` display and handle `500 RPC_CALL_FAIL` retries during backup finalization.
- Add `LastBackupError` tracking for better diagnostics in `TrayContext` and `AuthSettingsForm`.