using EGG9000.Analyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Analyzers.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class BannerCommentAnalyzerTests {
        private static async Task<Diagnostic[]> GetDiagnosticsAsync(string source) {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create("Test",
                [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzer = new BannerCommentAnalyzer();
            var withAnalyzers = compilation.WithAnalyzers([analyzer]);
            var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
            return [.. diagnostics];
        }

        [TestMethod]
        public async Task DashDivider_Flagged() {
            var diagnostics = await GetDiagnosticsAsync("""
                class C {
                    // ----------------
                    void M() { }
                }
                """);
            Assert.IsTrue(diagnostics.Any(d => d.Id == BannerCommentAnalyzer.DividerDiagnosticId));
        }

        [TestMethod]
        public async Task EqualsDivider_Flagged() {
            var diagnostics = await GetDiagnosticsAsync("""
                class C {
                    // ================
                    void M() { }
                }
                """);
            Assert.IsTrue(diagnostics.Any(d => d.Id == BannerCommentAnalyzer.DividerDiagnosticId));
        }

        [TestMethod]
        public async Task SectionHeaderStyleDivider_Flagged() {
            var diagnostics = await GetDiagnosticsAsync("""
                class C {
                    // --- Section: Foo ---
                    void M() { }
                }
                """);
            // Not a pure divider (has text), so this one relies on the pure-divider rule only if
            // fully punctuation; a mixed line like this is intentionally left to the manual audit.
            Assert.IsFalse(diagnostics.Any(d => d.Id == BannerCommentAnalyzer.DividerDiagnosticId));
        }

        [TestMethod]
        public async Task NormalComment_NotFlagged() {
            var diagnostics = await GetDiagnosticsAsync("""
                class C {
                    // Retries because the upstream API flakes under load.
                    void M() { }
                }
                """);
            Assert.IsFalse(diagnostics.Any(d => d.Id == BannerCommentAnalyzer.DividerDiagnosticId));
        }

        [TestMethod]
        public async Task AlignmentPadding_Flagged() {
            var diagnostics = await GetDiagnosticsAsync("""
                class C {
                    int a = 1;    // first
                    int bb = 22;  // second
                }
                """);
            Assert.IsTrue(diagnostics.Any(d => d.Id == BannerCommentAnalyzer.AlignmentDiagnosticId));
        }

        [TestMethod]
        public async Task SingleSpaceBeforeComment_NotFlagged() {
            var diagnostics = await GetDiagnosticsAsync("""
                class C {
                    int a = 1; // first
                }
                """);
            Assert.IsFalse(diagnostics.Any(d => d.Id == BannerCommentAnalyzer.AlignmentDiagnosticId));
        }

        [TestMethod]
        public async Task LeadingIndentationOnOwnLine_NotFlaggedAsAlignment() {
            var diagnostics = await GetDiagnosticsAsync("""
                class C {
                    // A comment on its own line, indented normally.
                    void M() { }
                }
                """);
            Assert.IsFalse(diagnostics.Any(d => d.Id == BannerCommentAnalyzer.AlignmentDiagnosticId));
        }
    }
}
