using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Site.Models;

public record LeaderboardChoice(string Label, string Href, bool Active, string Color = null);

public record LeaderboardMenu(string Caption, IReadOnlyList<LeaderboardChoice> Choices);

public record LeaderboardJump(string Anchor, string Label);

public record LeaderboardLead(int Rank, string Name, string Href = null);

public record LeaderboardHeader(string Title) {
    public List<LeaderboardMenu> Menus { get; } = [];
    public List<LeaderboardJump> Jumps { get; } = [];

    public LeaderboardHeader Menu(string caption, params IEnumerable<LeaderboardChoice> choices) {
        Menus.Add(new(caption, [.. choices]));
        return this;
    }

    public LeaderboardHeader Jump(IEnumerable<LeaderboardJump> jumps) {
        Jumps.AddRange(jumps);
        return this;
    }
}

public static class LeaderboardQuery {
    public static string WithQuery(this HttpRequest request, string key, string value) {
        var query = request.Query
            .Where(x => !string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Value.Select(v => KeyValuePair.Create(x.Key, v ?? "")))
            .ToList();
        query.Add(KeyValuePair.Create(key, value));
        return new QueryBuilder(query).ToQueryString().Value;
    }
}
