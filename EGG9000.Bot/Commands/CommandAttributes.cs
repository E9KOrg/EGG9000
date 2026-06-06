using Discord.WebSocket;
using EGG9000.Common.Database.Entities;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public enum StaffOnlyLevel {
        None = 0,
        ChickenTender = 1,
        FarmHand = 2,
        CluckingCoordinator = 3,
        Admin = 4
    };

    [AttributeUsage(AttributeTargets.Method)]
    public class UserCommandAttribute : Attribute {
        public string Name = "";
        public StaffOnlyLevel AdminOnly;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class SlashCommandAttribute : Attribute {
        public string Description = "";
        public StaffOnlyLevel AdminOnly = StaffOnlyLevel.None;
        public string ParentCommand { get; set; } = "";
        public bool AllowInDMs;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ComponentCommandAttribute : Attribute {
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ModalAttribute : Attribute {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class SlashParamAttribute : Attribute {
        public string Description = "";
        public bool Required = true;
        public Type AutocompleteHandler;
        public bool PositiveOnly = false;
        public int StringMaxLength = 6000;
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class ComponentDataAttribute : Attribute {
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

    public interface IAutoCompleteHandler {
        Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds);
    }
}
