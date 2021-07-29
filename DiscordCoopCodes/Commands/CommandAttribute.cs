using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordCoopCodes.Commands {
    public class CommandAttribute : System.Attribute {
        public string Command;
        public string ExampleParams;
        public string Description;
        public bool AdminOnly;
    }
}
