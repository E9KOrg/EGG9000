using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.JsonData;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class CoopNames {
        private const int MaxAttempts = 64;
        private const int MaxSecondWordAttempts = 16;

        private static readonly EmbeddedResource<List<string>> _words = EmbeddedResource.Json<List<string>>("coop-words.json");

        public static IReadOnlyList<string> Words => _words.Value;

        public static string Normalize(string name) => name.Trim().ToLowerInvariant();

        public static string RandomWord() => Capitalize(Words[Random.Shared.Next(Words.Count)]);

        public static string RandomSecondWord(string firstWord) {
            var last = char.ToLowerInvariant(firstWord[^1]);
            var avoidLi = last is 'l' or 'i';
            for(var attempt = 0; attempt < MaxSecondWordAttempts; attempt++) {
                var candidate = Words[Random.Shared.Next(Words.Count)];
                var first = candidate[0];
                var blocked = avoidLi ? first is 'l' or 'i' : first == last;
                if(!blocked)
                    return Capitalize(candidate);
            }
            return RandomWord();
        }

        public static string RandomNumber(bool wide = false) {
            string number;
            do {
                number = (wide ? Random.Shared.Next(100, 1000) : Random.Shared.Next(99)).ToString();
            } while(number.Contains("69", StringComparison.Ordinal));
            return number;
        }

        public static string Generate(string stem = null, bool stemIsComplete = false, bool wide = false) {
            var first = stem ?? RandomWord();
            var body = stemIsComplete ? first : first + RandomSecondWord(first);
            return body + RandomNumber(wide);
        }

        public static async Task<string> GenerateUnusedAsync(ApplicationDbContext db, string contractId, List<UserByAccount> accounts = null, Guild guild = null, CancellationToken ct = default) {
            var (stem, complete) = StemFor(accounts, guild);
            for(var attempt = 0; attempt < MaxAttempts; attempt++) {
                var name = Generate(stem, complete, wide: attempt >= MaxAttempts / 2);
                if(!await IsUsedAsync(db, contractId, name, ct))
                    return name;
            }
            throw new InvalidOperationException($"No unused co-op name found for contract '{contractId}' after {MaxAttempts} attempts.");
        }

        public static async Task<bool> IsUsedAsync(ApplicationDbContext db, string contractId, string name, CancellationToken ct = default) {
            var contract = contractId ?? UsedCoopName.AnyContract;
            var key = Normalize(name);
            var anyContract = contract == UsedCoopName.AnyContract;
            return await db.UsedCoopNames.AnyAsync(u => u.Name == key
                    && (anyContract || u.ContractID == contract || u.ContractID == UsedCoopName.AnyContract), ct)
                || await db.Coops.AnyAsync(c => c.Name.ToLower() == key
                    && (anyContract || c.ContractID == contract || c.ContractID == null), ct);
        }

        private static (string Stem, bool Complete) StemFor(List<UserByAccount> accounts, Guild guild) {
            var now = DateTimeOffset.UtcNow;
            var users = (accounts ?? []).Select(x => x.User).Where(u => u is not null).DistinctBy(u => u.Id).ToList();
            foreach(var user in users.Where(u => u.ExpireCustomCoopName < now)) {
                user.ExpireCustomCoopName = null;
                user.CustomCoopName = null;
            }

            var custom = users.Select(u => u.CustomCoopName).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if(custom.Count > 1)
                return (string.Concat(custom), true);
            if(custom.Count == 1)
                return (custom[0], custom[0].Length > 5 && custom[0].Count(char.IsUpper) > 1);
            return (string.IsNullOrWhiteSpace(guild?.CoopNamePrefix) ? null : guild.CoopNamePrefix, false);
        }

        private static string Capitalize(string word) => char.ToUpperInvariant(word[0]) + word[1..];
    }
}
