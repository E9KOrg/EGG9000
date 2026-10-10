using Discord.WebSocket;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_EditUserModel {
        public List<Admin_EditUserWithDetails> Users { get; set; }
        public List<IdentityRole> Roles { get; set; }
        public List<SocketGuild> DiscordGuilds { get; set; }
        public List<Guild> DbGuilds { get; set; }
    }

    public class Admin_EditUserWithDetails {
        public string CustomCoopName { get; set; }
        public DateTimeOffset? ExpireCustomCoopName { get; set; }
        public Guid DBUserId { get; set; }
        public string DiscordId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }
        public List<IdentityUserRole<string>> IdentityUserRoles { get; set; }
    }
}
