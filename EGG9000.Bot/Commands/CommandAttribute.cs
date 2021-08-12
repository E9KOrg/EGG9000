using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Bot.Commands {
    public class CommandAttribute : System.Attribute {
        public string Command;
        public string ExampleParams;
        public string Description;
        public bool AdminOnly;
    }
}
