using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Bot.Commands {
    [AttributeUsage(AttributeTargets.Method)]
    public class SlashCommandAttribute : System.Attribute {
        public string Description = "";
        public bool AdminOnly;
        public bool AllowFarmHand;
        public string ParentCommand { get; set; }
    }
    [AttributeUsage(AttributeTargets.Parameter)]
    public class SlashParamAttribute : System.Attribute {
        public string Description = "";
        public bool Required = true;
    }
}
