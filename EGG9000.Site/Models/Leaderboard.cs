using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Site.Models;

public record LeaderboardChoice(string Label, string Href, bool Active, string Color = null);

public record LeaderboardMenu(string Caption, IReadOnlyList<LeaderboardChoice> Choices);

public record LeaderboardLead(int Rank, string Name, string Href = null, bool IsMe = false) {
    public static LeaderboardLead For(Prefarm.LeaderboardUser user, int rank, string discordId, bool isAdmin) =>
        For(user.User, user.DisplayName, user.DisplayDiscordId, rank, discordId, isAdmin);

    public static LeaderboardLead For(DBUser user, string name, ulong rowDiscordId, int rank, string discordId, bool isAdmin) {
        var isMe = rowDiscordId.ToString() == discordId;
        string href = null;
        if(isAdmin) href = $"/Home/ViewUser/{user.Id}";
        else if(isMe) href = "/MyFarms";
        return new(rank, name, href, isMe);
    }

    public static string JumpLabel(Prefarm.LeaderboardUser user) {
        if(user.Backup?.UserName is { Length: > 0 } name) return name;
        if(user.Backup is not null && user.User.EggIncAccounts.FirstOrDefault(x => x.Id == user.Backup.EggIncId)?.Name is { Length: > 0 } account) return account;
        return user.DisplayName;
    }
}

public record LeaderboardHeader(string Title) {
    public List<LeaderboardMenu> Menus { get; } = [];
    public List<string> Jumps { get; } = [];

    public LeaderboardHeader Menu(string caption, params IEnumerable<LeaderboardChoice> choices) {
        Menus.Add(new(caption, [.. choices]));
        return this;
    }

    public LeaderboardHeader Jump(IEnumerable<string> labels) {
        Jumps.AddRange(labels);
        return this;
    }

    public static string MeAnchor(int n) => $"me{n}";
}
