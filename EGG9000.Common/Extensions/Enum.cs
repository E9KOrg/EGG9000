using System;
using System.Linq;
using System.Reflection;

namespace EGG9000.Common.Extensions {
    public static class Enums {
        /// <summary>
        ///     A generic extension method that aids in reflecting 
        ///     and retrieving any attribute that is applied to an `Enum`.
        /// </summary>
        public static TAttribute GetAttribute<TAttribute>(object enumValue)
                where TAttribute : Attribute {
            return enumValue.GetType()
                            .GetMember(enumValue.ToString())
                            .First()
                            .GetCustomAttribute<TAttribute>();
        }
    }
}
