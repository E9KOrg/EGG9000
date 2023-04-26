using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace EGG9000.Common.Commands {
    [AttributeUsage(AttributeTargets.Method)]
    public class UserCommandAttribute : System.Attribute {
        public string Name = "";
        public bool AdminOnly;
        public bool AllowFarmHand;
        public bool CPOnly;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class SlashCommandAttribute : System.Attribute {
        public string Description = "";
        public bool AdminOnly;
        public bool AllowFarmHand;
        public bool CPOnly;
        public string ParentCommand { get; set; }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ComponentCommandAttribute : System.Attribute {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class SlashParamAttribute : System.Attribute {
        public string Description = "";
        public bool Required = true;
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class ComponentDataAttribute : System.Attribute {
    }

    public class SlashCommandFunction : CommandFunctionBase {
        public SlashCommandAttribute Details { get; set; }
        public string Name { get; set; }
        public List<SlashCommandFunction> SubFunctions { get; set; }
    }
    public class UserCommandFunction : CommandFunctionBase {
        public UserCommandAttribute Details { get; set; }
        public string Name { get; set; }
    }
    public class ComponentCommandFunction : CommandFunctionBase {
        public string Name { get; set; }
        public ComponentCommandAttribute Details { get; set; }
    }

    public class CommandFunctionBase {
        public MethodInfo MethodInfo { get; set; }
        public ParameterInfo[] Parameters { get; set; }
    }

    public class ButtonObject {

    }
}
