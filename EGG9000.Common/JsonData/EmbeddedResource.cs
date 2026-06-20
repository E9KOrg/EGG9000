using Newtonsoft.Json;

using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EGG9000.Common.JsonData {
    public sealed class EmbeddedResource<T> {
        private readonly Lazy<T> _lazy;

        public EmbeddedResource(string fileSuffix, Func<Stream, T> parse, Func<T, T> postProcess = null) {
            _lazy = new Lazy<T>(() => {
                var assembly = typeof(EmbeddedResource).Assembly;
                var name = assembly.GetManifestResourceNames().SingleOrDefault(s => s.EndsWith(fileSuffix))
                    ?? throw new InvalidOperationException($"Embedded resource ending in '{fileSuffix}' not found in {assembly.GetName().Name}.");
                using var stream = assembly.GetManifestResourceStream(name);
                var value = parse(stream);
                return postProcess is null ? value : postProcess(value);
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public T Value => _lazy.Value;
    }

    public static class EmbeddedResource {
        public static EmbeddedResource<T> Json<T>(string fileSuffix, Func<T, T> postProcess = null) =>
            new(fileSuffix, stream => {
                using var reader = new StreamReader(stream);
                return JsonConvert.DeserializeObject<T>(reader.ReadToEnd());
            }, postProcess);

        public static EmbeddedResource<T> Csv<T>(string fileSuffix, Func<Stream, T> parse) =>
            new(fileSuffix, parse);
    }
}
