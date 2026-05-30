using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace EGG9000.Migrator;

internal static class EntityMigrator {
    private const int BatchSize = 500;

    // Cached per-type list of DateTimeOffset properties to normalize to UTC.
    private static readonly Dictionary<Type, PropertyInfo[]> _dtoProps = [];

    private static PropertyInfo[] GetDtoProps(Type t) {
        if (_dtoProps.TryGetValue(t, out var cached)) return cached;
        var props = t.GetProperties()
            .Where(p => p.CanRead && p.CanWrite &&
                        (p.PropertyType == typeof(DateTimeOffset) ||
                         p.PropertyType == typeof(DateTimeOffset?)))
            .ToArray();
        _dtoProps[t] = props;
        return props;
    }

    // Npgsql 6+ requires all timestamptz values to be UTC.
    // SQL Server stores DateTimeOffset with local offsets, so normalize before writing.
    private static void NormalizeToUtc<T>(List<T> batch) where T : class {
        var props = GetDtoProps(typeof(T));
        if (props.Length == 0) return;
        foreach (var entity in batch) {
            foreach (var prop in props) {
                if (prop.PropertyType == typeof(DateTimeOffset)) {
                    var v = (DateTimeOffset)prop.GetValue(entity)!;
                    if (v.Offset != TimeSpan.Zero)
                        prop.SetValue(entity, v.ToUniversalTime());
                } else {
                    var v = (DateTimeOffset?)prop.GetValue(entity);
                    if (v.HasValue && v.Value.Offset != TimeSpan.Zero)
                        prop.SetValue(entity, v.Value.ToUniversalTime());
                }
            }
        }
    }

    /// <summary>
    /// Streams all rows from <paramref name="source"/> into <paramref name="targetSet"/> in batches,
    /// preserving original LastModified timestamps (which ApplicationDbContext would otherwise
    /// overwrite via its tracking event handler).
    /// </summary>
    public static async Task Migrate<T>(
        IQueryable<T> source,
        ApplicationDbContext target,
        DbSet<T> targetSet,
        string label) where T : class {

        Console.Write($"  {label}... ");
        int total = 0;
        var batch = new List<T>(BatchSize);

        await foreach (var row in source.AsNoTracking().AsAsyncEnumerable()) {
            batch.Add(row);
            if (batch.Count >= BatchSize) {
                await Flush(batch, target, targetSet);
                total += batch.Count;
                batch.Clear();
                Console.Write($"\r  {label}... {total} rows");
            }
        }

        if (batch.Count > 0) {
            await Flush(batch, target, targetSet);
            total += batch.Count;
        }

        Console.WriteLine($"\r  {label}: {total} rows");
    }

    private static async Task Flush<T>(List<T> batch, ApplicationDbContext target, DbSet<T> targetSet) where T : class {
        // Normalize all DateTimeOffset values to UTC before writing - Npgsql requires offset 0.
        NormalizeToUtc(batch);

        // Capture LastModified after normalization so the restored value is already UTC.
        var timestamps = batch
            .OfType<ILastModified>()
            .Select(e => (entity: e, ts: e.LastModified))
            .ToList();

        targetSet.AddRange(batch);

        // Restore the original timestamps from the source DB.
        foreach (var (entity, ts) in timestamps)
            entity.LastModified = ts;

        await target.SaveChangesAsync();
        target.ChangeTracker.Clear();
    }
}
