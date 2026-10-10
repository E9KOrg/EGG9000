using System;

namespace EGG9000.Bot.Interactions {
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
    public sealed class NoAutoAckAttribute : Attribute;
}
