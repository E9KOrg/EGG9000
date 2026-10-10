using System;
using System.Linq;
using System.Reflection;

namespace EGG9000.Common.Extensions {
    public static class Enums {
        public static TAttribute GetAttribute<TAttribute>(object enumValue)
                where TAttribute : Attribute {
            return enumValue.GetType()
                            .GetMember(enumValue.ToString())
                            .First()
                            .GetCustomAttribute<TAttribute>();
        }
    }
}
