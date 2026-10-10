using EGG9000.Analyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Analyzers.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class LinqStringEqualsAnalyzerTests {

        private static async Task<Diagnostic[]> GetDiagnosticsAsync(string source) {
            var tree = CSharpSyntaxTree.ParseText(source);

            var refs = new List<MetadataReference> {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.IQueryable<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.StringComparison).Assembly.Location),
            };

            var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
            foreach(var path in trustedAssemblies.Split(Path.PathSeparator)) {
                refs.Add(MetadataReference.CreateFromFile(path));
            }

            var compilation = CSharpCompilation.Create("Test",
                [tree],
                refs.DistinctBy(r => r.Display),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzer = new LinqStringEqualsAnalyzer();
            var withAnalyzers = compilation.WithAnalyzers([analyzer]);
            var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
            return [.. diagnostics];
        }

        [TestMethod]
        public async Task Equals_WithStringComparison_InWhereLambda_OnIQueryable_Warning() {
            var diagnostics = await GetDiagnosticsAsync("""
                using System;
                using System.Linq;

                public class Test {
                    public void Run(IQueryable<string> query, string value) {
                        var result = query.Where(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
                    }
                }
                """);

            Assert.IsTrue(diagnostics.Any(d => d.Id == LinqStringEqualsAnalyzer.DiagnosticId));
        }

        [TestMethod]
        public async Task Equals_WithStringComparison_InJoinLambda_OnIQueryable_Warning() {
            var diagnostics = await GetDiagnosticsAsync("""
                using System;
                using System.Linq;

                public class Coop { public string Name { get; set; } }
                public class User { public string CoopName { get; set; } }

                public class Test {
                    public void Run(IQueryable<User> users, IQueryable<Coop> coops, string targetName) {
                        var result = users
                            .Join(coops, u => u.CoopName, c => c.Name, (u, c) => new { u, c })
                            .Where(x => x.c.Name.Equals(targetName, StringComparison.CurrentCultureIgnoreCase));
                    }
                }
                """);

            Assert.IsTrue(diagnostics.Any(d => d.Id == LinqStringEqualsAnalyzer.DiagnosticId));
        }

        [TestMethod]
        public async Task Equals_WithStringComparison_InSelectLambda_OnIQueryable_Warning() {
            var diagnostics = await GetDiagnosticsAsync("""
                using System;
                using System.Linq;

                public class Test {
                    public void Run(IQueryable<string> query, string value) {
                        var result = query.Select(s => s.Equals(value, StringComparison.InvariantCultureIgnoreCase));
                    }
                }
                """);

            Assert.IsTrue(diagnostics.Any(d => d.Id == LinqStringEqualsAnalyzer.DiagnosticId));
        }

        [TestMethod]
        public async Task Equals_WithStringComparison_OnIEnumerable_NoDiagnostic() {
            var diagnostics = await GetDiagnosticsAsync("""
                using System;
                using System.Collections.Generic;
                using System.Linq;

                public class Test {
                    public void Run(IEnumerable<string> list, string value) {
                        var result = list.Where(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
                    }
                }
                """);

            Assert.IsFalse(diagnostics.Any(d => d.Id == LinqStringEqualsAnalyzer.DiagnosticId));
        }

        [TestMethod]
        public async Task Equals_WithStringComparison_OutsideLambda_NoDiagnostic() {
            var diagnostics = await GetDiagnosticsAsync("""
                using System;

                public class Test {
                    public bool Run(string a, string b) {
                        return a.Equals(b, StringComparison.OrdinalIgnoreCase);
                    }
                }
                """);

            Assert.IsFalse(diagnostics.Any(d => d.Id == LinqStringEqualsAnalyzer.DiagnosticId));
        }

        [TestMethod]
        public async Task Equals_OneArgOverload_InIQueryableLambda_NoDiagnostic() {
            var diagnostics = await GetDiagnosticsAsync("""
                using System;
                using System.Linq;

                public class Test {
                    public void Run(IQueryable<string> query, string value) {
                        var result = query.Where(s => s.Equals(value));
                    }
                }
                """);

            Assert.IsFalse(diagnostics.Any(d => d.Id == LinqStringEqualsAnalyzer.DiagnosticId));
        }

        [TestMethod]
        public async Task Equals_WithStringComparison_AfterAsEnumerable_NoDiagnostic() {
            var diagnostics = await GetDiagnosticsAsync("""
                using System;
                using System.Linq;

                public class Test {
                    public void Run(IQueryable<string> query, string value) {
                        var result = query.AsEnumerable()
                                          .Where(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
                    }
                }
                """);

            Assert.IsFalse(diagnostics.Any(d => d.Id == LinqStringEqualsAnalyzer.DiagnosticId));
        }
    }
}
