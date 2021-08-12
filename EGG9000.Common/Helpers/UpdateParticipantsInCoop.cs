using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Helpers {
    public static class UpdateParticipantsInCoop {
        public static async System.Threading.Tasks.Task UpodateAsync(List<DBUser> users, ApplicationDbContext _db, Coop coop, Ei.ContractCoopStatusResponse status, ITextChannel coopChannel, DiscordSocketClient _client) {
            foreach (var p in status.Participants) {
                //See if user already is in coop
                var xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.User.UserMatchesProto(p));

                if (xref == null) {
                    var user = users.FirstOrDefault(x => x.EggIncIds.Any(x => x.Id == p.UserId));
                    if (user != null) {
                        if (coopChannel != null) {
                            var discoordUser = _client.GetUser(user.DiscordId); ;
                            if (discoordUser != null) {
                                try {
                                    await coopChannel.AddPermissionOverwriteAsync(discoordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
                                } catch {
                                    Console.WriteLine("Error adding permission");
                                }
                            }
                        }

                        xref = new UserCoopXref { CoopId = coop.Id, UserId = user.Id, CreatedOn = DateTimeOffset.Now, AddedToChannel = true, EggIncId = p.UserId };
                        _db.UserCoopXrefs.Add(xref);
                        Console.WriteLine("Adding user xref");

                        //Make sure we have the users updated name and id
                        user.UpdateNameAndId(p);
                    }
                }
            }
        }
    }
}
