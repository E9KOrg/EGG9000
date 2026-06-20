using EGG9000.Bot;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    // One precomputed cell in a colleggtible row: a level's modifier plus where the player sits relative to it.
    public record ColleggtibleCell(string Percent, double RawValue, bool HasValue, bool Active, bool Future);

    // A fully resolved colleggtible row ready to render: no formatting or level logic left for the view.
    public record ColleggtibleRow(
        string Name,
        string IconUrl,
        string Dimension,
        int Level,
        bool Missing,
        string FarmReachedText,
        IReadOnlyList<ColleggtibleCell> Cells);

    // Builds the colleggtible table model from a backup + the custom-egg catalog. All level, sort,
    // dimension, percent, and population formatting lives here so views (and the bot) just render.
    public static class ColleggtibleHelper {
        public static List<ColleggtibleRow> BuildRows(CustomBackup backup, List<DBCustomEgg> customEggs) {
            if(customEggs is null)
                return [];

            return customEggs
                .Select(egg => BuildRow(backup, egg))
                .OrderBy(r => r.Missing ? 1 : 0)
                .ThenBy(r => r.Name)
                .ToList();
        }

        private static ColleggtibleRow BuildRow(CustomBackup backup, DBCustomEgg egg) {
            var (level, farmSize) = backup?.GetColleggtibleProgress(egg.Identifier) ?? (0u, 0UL);
            var intLevel = (int)level;
            var dimension = egg.Modifiers?.FirstOrDefault()?.DimensionName() ?? "";
            var farmReachedText = intLevel < 4 && farmSize > 0 ? farmSize.ToEggString() : "";

            var cells = new List<ColleggtibleCell>(4);
            for(var l = 0; l < 4; l++) {
                var mod = egg.Modifiers != null && egg.Modifiers.Count > l ? egg.Modifiers[l] : null;
                cells.Add(new ColleggtibleCell(
                    Percent: mod?.PercentString() ?? "",
                    RawValue: mod?.Value ?? 0,
                    HasValue: mod != null,
                    Active: (l + 1) == intLevel,
                    Future: (l + 1) > intLevel));
            }

            return new ColleggtibleRow(
                Name: egg.Name.ToLowerInvariant().FirstCharToUpper(),
                IconUrl: egg.Icon?.URL ?? "",
                Dimension: dimension,
                Level: intLevel,
                Missing: intLevel == 0,
                FarmReachedText: farmReachedText,
                Cells: cells);
        }
    }
}
