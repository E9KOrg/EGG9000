using Discord.WebSocket;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.JsonData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Bot {
    public class Words
    {
        private readonly Random _rnd;

        public Words()
        {
            _rnd = new Random();
        }

        public string GetRandomWord()
        {
            return FirstCharToUpper(WordList[_rnd.Next(WordList.Count)]);
        }

        // Pick a second word that does not start with the first word's last character (avoids
        // double-letter mashups like "BankKite"). The old version allocated a full filtered copy of
        // the ~1600-word list on every coop creation; rejection sampling is allocation-free and the
        // excluded letter only trims a few percent, so it converges in ~1 try. Bounded retries guard
        // the degenerate case where the pool somehow lacks any other starting letter.
        public string GetRandomSecondWord(string firstWord)
        {
            var exclude = char.ToLowerInvariant(firstWord.Last());
            for(var attempt = 0; attempt < 16; attempt++) {
                var candidate = WordList[_rnd.Next(WordList.Count)];
                if(candidate.Length == 0 || char.ToLowerInvariant(candidate[0]) != exclude)
                    return FirstCharToUpper(candidate);
            }
            return FirstCharToUpper(WordList[_rnd.Next(WordList.Count)]);
        }

        public string GetRandomNumber()
        {
            int number;
            do { number = _rnd.Next(99); } while(number == 69);
            return number.ToString();
        }

        public static string FirstCharToUpper(string input)
        {
            switch (input)
            {
                case null:
                case "":
                    return input;
                default: return input.First().ToString().ToUpper() + input[1..];
            }
        }

        public string GetCoopName(List<UserByAccount> prefarms, SocketGuild discordguild, Guild dbguild) {
            var customNames = prefarms.Where(x => !string.IsNullOrEmpty(x.User?.CustomCoopName)).GroupBy(x => x.User.Id).ToList();
            var customNamesExpired = customNames.Where(x => x.First().User.ExpireCustomCoopName.HasValue && x.First().User.ExpireCustomCoopName.Value < DateTimeOffset.UtcNow);
            foreach(var customName in customNamesExpired) {
                customName.First().User.ExpireCustomCoopName = null;
                customName.First().User.CustomCoopName = null;
            }
            customNames = customNames.Where(x => !string.IsNullOrEmpty(x.First().User.CustomCoopName)).ToList();

            if(customNames.Count > 1) {
                return string.Join("", customNames.Select(x => x.First().User.CustomCoopName)) + GetRandomNumber();
            } 
            if(customNames.Count == 1) {
                var name = customNames.First().First().User.CustomCoopName;

                if(name.Count(c => char.IsUpper(c)) > 1 && name.Length > 5) {
                    return name + GetRandomNumber();
                } else {
                    return name + GetRandomSecondWord(name) + GetRandomNumber();
                }
            } else {
                if(!string.IsNullOrWhiteSpace(dbguild.CoopNamePrefix))
                    return $"{dbguild.CoopNamePrefix}{GetRandomSecondWord(dbguild.CoopNamePrefix)}{GetRandomNumber()}";
                var wordOne = GetRandomWord();
                var wordTwo = GetRandomSecondWord(wordOne);
                return wordOne + wordTwo + GetRandomNumber();
            }
        }

        private List<string> WordList => CoopWords.Get();
    }

}
