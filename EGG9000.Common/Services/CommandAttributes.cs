using Discord.WebSocket;

using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Common.Commands {

    public enum StaffOnlyLevel {
        None = 0,
        ChickenTender = 1,
        FarmHand = 2,
        CluckingCoordinator = 3,
        Admin = 4
    };

    [AttributeUsage(AttributeTargets.Method)]
    public class UserCommandAttribute : System.Attribute {
        public string Name = "";
        public StaffOnlyLevel AdminOnly;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class SlashCommandAttribute : System.Attribute {
        public string Description = "";
        public StaffOnlyLevel AdminOnly = StaffOnlyLevel.None;
        public string ParentCommand { get; set; } = "";
        public bool AllowInDMs;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ComponentCommandAttribute : System.Attribute {
        public StaffOnlyLevel AdminOnly = StaffOnlyLevel.None;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ModalAttribute : System.Attribute {
        public StaffOnlyLevel AdminOnly = StaffOnlyLevel.None;
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class SlashParamAttribute : System.Attribute {
        public string Description = "";
        public bool Required = true;
        public Type AutocompleteHandler;
        public bool PositiveOnly = false;
        public int StringMaxLength = 6000;
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class ComponentDataAttribute : System.Attribute {
    }

    public class SlashCommandFunction : CommandFunctionBase {
        public SlashCommandAttribute Details { get; set; }
        public List<SlashCommandFunction> SubFunctions { get; set; }
        public override StaffOnlyLevel AdminOnly => Details?.AdminOnly ?? StaffOnlyLevel.None;
    }
    public class UserCommandFunction : CommandFunctionBase {
        public UserCommandAttribute Details { get; set; }
        public override StaffOnlyLevel AdminOnly => Details?.AdminOnly ?? StaffOnlyLevel.None;
    }
    public class ComponentCommandFunction : CommandFunctionBase {
        public ComponentCommandAttribute Details { get; set; }
        public override StaffOnlyLevel AdminOnly => Details?.AdminOnly ?? StaffOnlyLevel.None;
    }
    public class ModalCommandFunction : CommandFunctionBase {
        public ModalAttribute Details { get; set; }
        public override StaffOnlyLevel AdminOnly => Details?.AdminOnly ?? StaffOnlyLevel.None;
    }


    public class CommandFunctionBase {
        public MethodInfo MethodInfo { get; set; }
        public ParameterInfo[] Parameters { get; set; }
        public string Name { get; set; }
        public virtual StaffOnlyLevel AdminOnly => StaffOnlyLevel.None;
    }


    public class ButtonObject {

    }

    public interface IAutoCompleteHandler {
        public Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds);
    }
}
