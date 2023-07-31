using Discord.Interactions;
using Discord.WebSocket;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Commands {
    [AttributeUsage(AttributeTargets.Method)]
    public class UserCommandAttribute : System.Attribute {
        public string Name = "";
        public bool AdminOnly;
        public bool AllowFarmHand;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class SlashCommandAttribute : System.Attribute {
        public string Description = "";
        public bool AdminOnly = false;
        public bool AllowFarmHand = false;
        public string ParentCommand { get; set; } = "";
        public bool AllowInDMs;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ComponentCommandAttribute : System.Attribute {
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ModalAttribute : System.Attribute {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class SlashParamAttribute : System.Attribute {
        public string Description = "";
        public bool Required = true;
        public Type AutocompleteHandler;
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class ComponentDataAttribute : System.Attribute {
    }

    public class SlashCommandFunction : CommandFunctionBase {
        public SlashCommandAttribute Details { get; set; }
        public List<SlashCommandFunction> SubFunctions { get; set; }
    }
    public class UserCommandFunction : CommandFunctionBase {
        public UserCommandAttribute Details { get; set; }
    }
    public class ComponentCommandFunction : CommandFunctionBase {
        public ComponentCommandAttribute Details { get; set; }
    }
    public class ModalCommandFunction : CommandFunctionBase {
        public ModalAttribute Details { get; set; }
    }


    public class CommandFunctionBase {
        public MethodInfo MethodInfo { get; set; }
        public ParameterInfo[] Parameters { get; set; }
        public string Name { get; set; }
    }


    public class ButtonObject {

    }

    public interface AutoCompleteHandler {
        public Task Run(SocketAutocompleteInteraction arg);
    }
}
