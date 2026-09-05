using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.ConvertProbe {
    public static class AccountDecoder {
        public const int BatchSize = 500;

        public sealed record UserBlob(Guid Id, ulong DiscordId, byte[] Blob);

        public sealed class DecodeResult {
            public List<EggIncAccount> Accounts { get; init; }
            public Exception Error { get; init; }
            public bool Ok => Error is null;
        }

        public static string FormatOf(byte[] blob) {
            if(blob is null) return "null";
            if(blob.Length == 0) return "empty";
            return blob[0] switch {
                StorageCompression.Marker => "envelope",
                0x1F => "gzip",
                _ => "legacy"
            };
        }

        public static string AlgoOf(byte[] blob) {
            if(blob is not { Length: >= 2 } || blob[0] != StorageCompression.Marker) return "";
            return AlgoName(blob[1]);
        }

        public static string AlgoName(byte algo) {
            return Enum.IsDefined(typeof(StorageCompressionAlgorithm), algo)
                ? ((StorageCompressionAlgorithm)algo).ToString().ToLowerInvariant()
                : $"0x{algo:X2}";
        }

        public static DecodeResult Decode(byte[] blob) {
            try {
                return new DecodeResult { Accounts = StorageCodec.Unpack<List<EggIncAccount>>(blob) ?? [] };
            } catch(Exception e) {
                return new DecodeResult { Error = e };
            }
        }

        public static async IAsyncEnumerable<UserBlob> StreamUsersAsync(ApplicationDbContext db, int? limit) {
            var query = db.DBUsers.AsNoTracking().Where(u => u._contractRegistrationByte != null).OrderBy(u => u.Id);
            var yielded = 0;
            for(var skip = 0; ; skip += BatchSize) {
                var take = limit.HasValue ? Math.Min(BatchSize, limit.Value - yielded) : BatchSize;
                if(take <= 0) yield break;
                var batch = await query.Skip(skip).Take(take)
                    .Select(u => new UserBlob(u.Id, u.DiscordId, u._contractRegistrationByte))
                    .ToListAsync();
                foreach(var user in batch) {
                    yielded++;
                    yield return user;
                }
                if(batch.Count < take) yield break;
            }
        }
    }
}
