using Discord;
using Discord.WebSocket;
using EGG9000.Common.Commands;
using EGG9000.Common.Database.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class FAQHelper {

        public static readonly List<FAQItem> _FaqTopics = new();

        public class FAQBuilder {
            public ComponentBuilder ComponentBuilder { get; set; }
            public EmbedBuilder EmbedBuilder { get; set; }
            public FAQBuilder() { }
        }

        public class FAQItem {
            public string Name { get; set; }
            public List<string> Keywords { get; set; } = new();
            public string Explanation { get; set; } = string.Empty;
            public StaffOnlyLevel StaffOnlyLevel { get; set; } = StaffOnlyLevel.None;
            public Func<Guild, SocketGuild, bool> ApplicableToGuild { get; set; } = new Func<Guild, SocketGuild, bool>((guild, socketGuild) => true);
            public Color EmbedColor { get; set; } = Color.DarkGrey;
            public Guild OwnerGuild { get; set; } = null;
        }

        public static void Populate() {
            //Contract eggspert
            _FaqTopics.Add(new FAQItem {
                Name = "Contract Eggspert Role",
                Keywords = new List<string> { "contract eggspert", "eggspert", "carry contract"},
                Explanation = "The `💪 Contract Eggspert 💪` role is added to users who get an eggceptional score on a certain contract. The role lasts for 7 days after scoring takes place.\n\n" +
                "The number next to players' names in the announcement reflects how many times (`x`) more eggs they delivered than the 100 closest players to their EB.",
                ApplicableToGuild = new Func<Guild, SocketGuild, bool>((guild, socketGuild) => {
                    try {
                        return socketGuild.GetRole(938563459812049008) != null;
                    } catch(Exception ex) {
                        Console.WriteLine($"Exception in FAQ (Contract Eggspert Role): " + ex.Message);
                        return false;
                    }
                })
            });

            //General scoring + Running score
            _FaqTopics.Add(new FAQItem {
                Name = "Scoring & Running Score",
                Keywords = new List<string> { "score", "running score", "rsc", "scoring", "contract score"},
                Explanation = "Contracts are scored when the contract itself has expired (no new coops can be started), and all server co-ops have finished, failed, or expired.\n\n" +
                "During contract scoring, your 'Eggs Delivered' count is compared to the 100 players closest to you in Earnings Bonus (EB) - this being the 50 players below you, and the 50 players above you.\nExample scores:\n" +
                "- A score of `1.0` means you delivered exactly the average\n- A score of `0.1` means you delivered 1/10th of the average\n- A score of `0.01` means you delivered 1/100th of the average\n\n" +
                "After you have been scored for 4 contracts, a Running Score will start to be calculated. This score is the average of your last 4 scored contracts, and updates every time a new contract is scored.\n" +
                "Running score is also the metric that Staff use to track players who are not pulling their weight.\n\nYou can check individual scores, as well as running score at https://egg9000.com/MyFarms"
            });

            _FaqTopics.Add(new FAQItem {
                Name = "Test one",
                Keywords = new List<string> { "test" },
                Explanation = "Test one\n\nThis should be displaying with a blue embed",
                EmbedColor = Color.Blue
            });

            _FaqTopics.Add(new FAQItem {
                Name = "Test two",
                Keywords = new List<string> { "test two", "test" },
                Explanation = "Test two\n\nThis should be displaying with an orange embed",
                EmbedColor = Color.Orange
            });

            _FaqTopics.Add(new FAQItem {
                Name = "Test three",
                Keywords = new List<string> { "test", "three", "test three", "test", "test", "test"},
                Explanation = "Test three\n\nThis should be displaying with a red embed",
                EmbedColor = Color.Red
            });

            _FaqTopics.Add(new() {
                Name = "Test Staff Template",
                Keywords = new List<string> { "staff" },
                Explanation = "Staff template 123 xyz",
                EmbedColor = Color.Purple,
                StaffOnlyLevel = StaffOnlyLevel.ChickenTender
            });
        }
    }
}
