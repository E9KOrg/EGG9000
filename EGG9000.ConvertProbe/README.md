# EGG9000.ConvertProbe

Read-only console probe that measures and proves the lazy storage-format conversion (accounts blob envelope, coop status proto) on a prod clone.

Read-only guarantee: every connection starts with `default_transaction_read_only=on`, EF runs `NoTracking`, nothing calls `SaveChanges`.

Usage: `dotnet run --project EGG9000.ConvertProbe -- <verb> --conn "<npgsql>" [--out dist/convertprobe] [--limit N]`

- `all`: one-shot data collection into a timestamped `<out>/<yyyyMMdd-HHmmssZ>/` directory: `formats`, `coverage`, `goalsets`, `bench --limit 500`, `query-demo`, `dump`, plus `summary.md` and an appended `formats-timeline.csv`. Run this before and after a rehearsal.
- `formats --csv dist/formats.csv`: first-byte histograms of both blob columns (Users split by UpdateBackups reachability: disabled, no guild, unknown guild, stale, fresh; Coops by the ThreadsCoopStatusUpdater polling predicate: polled vs not polled), `_response` presence, relation sizes, last-24h AutomationLogs; `--csv` appends buckets with a UTC timestamp.
- `coverage`: decodes every accounts blob via `StorageCodec.Unpack`, reports EiBackup/Simulation/LocalContract presence, writes `coverage.csv`.
- `inspect [--all]`: for every accounts blob that fails `StorageCodec.Unpack` (or every blob with `--all`), decompresses to plain msgpack (envelope, gzip, LZ4 block array, LZ4 block, or raw) and walks it with `MessagePackReader` against a reflection-built `[Key(n)]` schema rooted at `EggIncAccount`; reports overflow (stored integer outside the declared type's range), type mismatch, nil in a value-typed slot, non-finite floats, retired/extra slots, with path, declared type, msgpack code and raw value; summary by path; writes `inspect.csv`. Not part of `all`.
- `goalsets`: parses every `Contracts._response`, lists contracts whose GoalSets have differing goal counts, then scans archived and active farms with Grade unset and League >= 1 to count how many would have Completed flip under `GoalSets[League]` (stack) vs `GoalSets[0]` (master); writes `goalsets.csv` with masked ids.
- `dump`: canonical JSON of every CustomBackup to `<out>/dump/<DiscordId>_<EggIncId>.json`.
- `diff <dirA> <dirB> [--volatile regexes.txt]`: compares two dumps (farms keyed by ContractId), filters volatile paths, writes `diff.csv`.
- `bench --limit 200`: decode/encode timings and sizes for legacy (`StorageMessagePack.Options`), envelope, and master-equivalent (`Standard.WithCompression(Lz4BlockArray)`) encodes, plus an LZ4 block layout table (blocks per blob, distinct uncompressed block lengths) for stored, legacy, and master-equivalent bytes; writes `bench.md`.
- `query-demo`: LINQ over `Backup.EiBackup.Game.LifetimeCashEarned`, a field no legacy projection ever stored.

Exit codes: 0 ok, 1 usage/connection error, 2 coverage or inspect decode failure, 3 unexplained diff.
