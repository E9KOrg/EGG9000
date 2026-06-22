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
        private readonly List<string> _wordList;

        public Words()
        {
            _rnd = new Random();
            _wordList = WordList;
        }

        public string GetRandomWord()
        {
            return FirstCharToUpper(WordList[_rnd.Next(WordList.Count)]);
        }

        public string GetRandomSecondWord(string firstWord)
        {
            var FilteredWordList = WordList.FindAll(e => !e.StartsWith(firstWord.Last()));
            return FirstCharToUpper(FilteredWordList[_rnd.Next(FilteredWordList.Count)]);
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
