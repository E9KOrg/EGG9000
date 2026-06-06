using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace EGG9000.Bot.Services {
    public static class AssemblyExtensions {
        // GetExportedTypes() throws hard if any exported type fails to load (e.g. a
        // third-party assembly compiled against a different dependency ABI). Reflection
        // scans over AppDomain assemblies must not let one bad assembly crash startup.
        public static IEnumerable<Type> GetLoadableExportedTypes(this Assembly assembly) {
            try {
                return assembly.GetExportedTypes();
            } catch(ReflectionTypeLoadException ex) {
                return ex.Types.Where(t => t is not null && t.IsPublic)!;
            } catch(Exception ex) when(ex is TypeLoadException or FileLoadException or FileNotFoundException) {
                return [];
            }
        }
    }
}
