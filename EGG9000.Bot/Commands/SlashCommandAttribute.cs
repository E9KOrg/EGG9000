using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Bot.Commands {
    [AttributeUsage(AttributeTargets.Method)]
    public class ContextCommandAttribute : System.Attribute {
        public string Name = "";
        public bool AdminOnly;
        public bool AllowFarmHand;
        public bool CPOnly;
        public string ParentCommand { get; set; }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class SlashCommandAttribute : System.Attribute {
        public string Description = "";
        public bool AdminOnly;
        public bool AllowFarmHand;
        public bool CPOnly;
        public string ParentCommand { get; set; }
    }
    [AttributeUsage(AttributeTargets.Parameter)]
    public class SlashParamAttribute : System.Attribute {
        public string Description = "";
        public bool Required = true;
    }
}
