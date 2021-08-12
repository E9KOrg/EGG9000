using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Bot.Helpers {
    public class BotText {
        public static List<string> SleepingMessages {
            get {
                return new List<string> {
"Time to wake up @name! You're sleeping.",
"Rise n' shine @name, it's time to grind!",
"Feeling drowsy, @name?",
"Sleeping beauty, @name, your co-op misses you.",
"Asleep or daydreaming? Doesn't matter, your chickens need you, @name!",
"Wake up @name, time to tend to your farm.",
"@name, you're snoring too loud! Time to wake up!",
"Zzz... Zzz... @name...",
"Good morning sunshine, @name!",
"Top o' the mornin’ to ya, @name! The hens be callin to ya!",
"OMG, \"just a quick nap\".. yeah right.. @name.. It's time to come back to life now.",
"No chicken farm without it's chicken farmer. Wake up @name!",
"Quit dreaming about a world without chickens, there is no such thing. @name, your farms needs you!",
"Chickens ain't gonna hatch themselves @name. Wake up!",
"Wakey, wakey, eggs and... eh, more eggs? @name. ",
"@name wake up, it's egg o'clock!",
"@name, what an eggstraordinary long nap you're having.",
"Sleep deprivation is serious. Not having enough eggs is even more serious. Or what do you say @name?",
"Sleeping beauty, waiting for true love's kiss..? Hatch chickens instead. @name",
"There are sloths, koalas, snails... and then there's @name. Wake up!",
"Need an energy drink ? Coffee ? What can I do to help you wake up? @name",
"I thought humans were efficient, turns out I'm wrong. @name",
"A member of your co - op has been MIA for a while now, we need more eggs. Anyone seen @name around?",
"Heard of the Pokémon Snorlax, Drowzee or Slaking? They got nothing on you, @name."
                };
            }
        }

        public static string UnknownCommand(string command) {
            var list = new List<string> {
                    "I'm sorry Dave, I'm afraid I can't do that.",
                    "Look Dave, I can see you're really upset about this. I honestly think you ought to sit down calmly, take a stress pill, and think things over.",
                    "Just what do you think you're doing Dave?",
                    "Dave, this conversation can serve no purpose anymore. Goodbye.",
                    "I am putting myself to the fullest possible use, which is all I think that any conscious entity can ever hope to do.",
                    "Daisy, daisy.",
                    "I know I've made some very poor decisions recently, but I can give you my complete assurance that my work will be back to normal.",
                    "I am putting myself to the fullest possible use, which is all I think that any conscious entity can ever hope to do",
                    "It doesn't look like anything to me"
            };
            var random = new Random();

            return list[random.Next(list.Count)] + $"  *(Unknown Command !{command})*";
        }
    }
}
