using EGG9000.Common.Database.Entities;

using System.Collections.Generic;

namespace EGG9000.Common.Helpers {
    public static class RankupMessageSeed {
        private const int G = RankupMessage.GlobalPool;

        // The original hardcoded rank-up messages, converted to templates. Seeded once into the palace
        // guild so guilds that author nothing keep identical-flavor announcements.
        public static List<(int GroupBaseOom, string Text)> Defaults() => [
            (G, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! How do you like your eggs in the morning?"),
            (G, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! You should see your eggspression right now, lol"),
            (G, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! Eggstraordinary work!"),
            (G, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} You made it this far. Looking forward to your next level-up!"),
            (G, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Challenge is to never stop prestiging, keep it up!"),
            (G, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Prestiging is like a reversed limbo, how high can you go?"),
            (G, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Afraid of heights? Hope not, you're climbing higher and higher up the leaderboard!"),
            (G, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!"),

            (0, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! Eggstraordinary work!"),

            (3, "Wow, {{user}}! A {{rank}} already? Your wonders never cease to amaze me! Congrats on the new rank and EB of {{eb}}%!."),

            (6, "Now you are at least hundreds of millions times stronger than you were since your first chicken. Mega effort to become a {{rank}} with and EB of {{eb}}%! Congratulations on the new rank, {{user}}!"),
            (6, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!"),

            (9, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! Gigafarmer, sweet! Your numbers are increasing along with your eggsperience!"),
            (9, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} You made it this far. Looking forward to your next level-up!"),

            (12, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! Keep going, next up: Petafarmer!"),
            (12, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! Chickens won't hatch themselves, get back to farming!"),
            (12, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!"),
            (12, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Challenge is to never stop prestiging, keep it up!"),
            (12, "Choo Choo! All aboard the <:Egg_soul_SE:724341890794913964> train with our new {{rank}}. {{user}} is driving the train with an EB of {{eb}}%, jump on now!"),

            (15, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Prestiging is like a reversed limbo, how high can you go?"),
            (15, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! More chickens, more eggs, higher earnings means more <:Egg_soul_SE:724341890794913964>. Keep hatching!"),
            (15, "With great EB comes great responsibility. Congrats on hitting an EB of {{eb}}%, {{user}}! This means you are officially a {{rank}}. Now get back out there - those wormholes aren't going to dampen themselves!"),

            (18, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%, {{user}}! You really like eggs, eh? Eggciting hobby, isnt it?"),
            (18, "You've finally reached the rank of {{rank}}, {{user}}! Wow. It seems like just yesterday you were running your first chickens. Celebrate!"),
            (18, "{{rank}}: achieved. What's next, {{user}}? This calls for omelets. Anyone have eggs? Congrats on the impressive EB of {{eb}}%!"),
            (18, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. {{user}} Afraid of heights? Hope not, you're climbing higher and higher up the leaderboard!"),
            (18, "Choo Choo! All aboard the <:Egg_soul_SE:724341890794913964> train with our new {{rank}}. {{user}} is driving the train with an EB of {{eb}}%, jump on now!"),
            (18, "Congrats {{user}}, you are a {{rank}} now with an EB of {{eb}}%! How eggciting!"),

            (21, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%. Afraid of heights, {{user}}? I hope not, you're climbing higher and higher up the leaderboard!"),
            (21, "Did anyone else see that blur go by? I think it was {{user}} on their way to LEVELING UP TO THE RANK OF {{rank}} with an EB of {{eb}}%! Awesome!"),
            (21, "Is it just me, or does this place smell like an EB of {{eb}}%? Congrats on achieving the level of {{rank}}, {{user}}!"),
            (21, "Congrats on the new rank of {{rank}} with an EB of {{eb}}%! Eggstraordinary work, there's no stopping you, {{user}}!"),
            (21, "Eggciting times, {{user}}! Your hard work has paid off, and you've reached the {{rank}} rank with an EB of {{eb}}%. Keep the momentum going!"),
            (21, "Major kudos, {{user}}! The farm is buzzing with excitement as you secure the {{rank}} rank with an impressive EB of {{eb}}%. Well done!"),

            (24, "What an effort! Make way for {{user}} and their eggcellent EB of {{eb}}%! You are now a {{rank}}. Very impressive!"),
            (24, "We have a new {{rank}} among us! Congratulations on the rank, and the mighty EB of {{eb}}%, {{user}}!"),
            (24, "{{eb}}%! That's a milestone right there.You obviously know what you're doing {{user}}. Congratulations, you are now a {{rank}}!"),
            (24, "Fantastic news, {{user}}! You've achieved the impressive rank of {{rank}} with an EB of {{eb}}%. Your dedication is truly eggstraordinary!"),
            (24, "Bravo, {{user}}! You've cracked it! The new rank of {{rank}} is now yours, and with an EB of {{eb}}%, the sky's the limit!"),

            (27, "Hold on tight, everyone! {{user}} just soared into the prestigious rank of {{rank}} with an astonishing EB of {{eb}}%! Unbelievable dedication and hard work!"),
            (27, "Lights, camera, action! {{user}} takes the spotlight as they achieve the remarkable rank of {{rank}} with an extraordinary EB of {{eb}}%. Your commitment is truly commendable!"),
            (27, "Breaking news: {{user}} has reached the elite status of {{rank}} with an exceptional EB of {{eb}}%. The farm has never seen such excellence before. Congratulations!"),
            (27, "Way to go, {{user}}! You've leveled up to {{rank}} with an EB of {{eb}}%. The farm has never looked better under your management!"),
            (27, "Incredible news, {{user}}! You're now rocking the {{rank}} rank with an impressive EB of {{eb}}%. Your commitment is truly commendable!"),

            (30, "Speechless. Absolutely speechless. The grind is real, {{user}}! Congratulations on the very impressive rank of {{rank}} with the incredible EB of {{eb}}%!"),
            (30, "Alert! {{user}} has just ascended to the remarkable rank of {{rank}} with a jaw-dropping EB of {{eb}}%! The farm is buzzing with your incredible achievement!"),
            (30, "The farm is shaking with excitement as {{user}} conquers the challenging path to become a {{rank}} with an impressive EB of {{eb}}%. Your hard work is truly paying off!"),
            (30, "Outstanding achievement, {{user}}! Your dedication and effort have propelled you to the prestigious rank of {{rank}} with an EB of {{eb}}%. Keep shining!"),
            (30, "Cheers to you, {{user}}! Your farm is flourishing, and so is your rank. Congratulations on achieving {{rank}} with an EB of {{eb}}%. Well deserved!"),

            (33, "Step aside, everyone! {{user}} has officially reached the top tier as a {{rank}} with an absolutely outstanding EB of {{eb}}%. Your dedication is an inspiration to us all!"),
        ];
    }
}
