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
    public class InteractionAckAnalyzerTests {

        private const string Scaffold = """
            using System;
            using System.Threading.Tasks;

            namespace Discord.Interactions {
                [AttributeUsage(AttributeTargets.Method)] public class ComponentInteractionAttribute : Attribute { public ComponentInteractionAttribute(string id, bool ignoreGroupNames = false) { } }
                [AttributeUsage(AttributeTargets.Method)] public class SlashCommandAttribute : Attribute { public SlashCommandAttribute(string name, string description) { } }
                [AttributeUsage(AttributeTargets.Method)] public class ModalInteractionAttribute : Attribute { public ModalInteractionAttribute(string id, bool ignoreGroupNames = false) { } }
            }

            namespace EGG9000.Bot.Interactions {
                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)] public class NoAutoAckAttribute : Attribute { }

                public class Interaction {
                    public bool HasResponded { get; }
                    public Task RespondAsync(string text = null, bool ephemeral = false) => Task.CompletedTask;
                    public Task RespondWithModalAsync(object modal) => Task.CompletedTask;
                    public Task DeferAsync(bool ephemeral = false) => Task.CompletedTask;
                    public Task UpdateAsync(Action<object> props) => Task.CompletedTask;
                    public Task ModifyOriginalResponseAsync(Action<object> props) => Task.CompletedTask;
                    public Task FollowupAsync(string text = null, bool ephemeral = false) => Task.CompletedTask;
                    public Task DeferDisablingAsync() => Task.CompletedTask;
                    public Task RejectAsync(string reason) => Task.CompletedTask;
                    public Task RestoreComponentsAsync() => Task.CompletedTask;
                    public Task RespondAsyncGettingMessage(string text = null) => Task.CompletedTask;
                }

                public class Ctx { public Interaction Interaction { get; } = new(); }

                public abstract class E9KModuleBase { public Ctx Context { get; } = new(); }
            }
            """;

        private static async Task<Diagnostic[]> GetDiagnosticsAsync(string handlers) {
            var source = Scaffold + """

                namespace Test {
                    using Discord.Interactions;
                    using EGG9000.Bot.Interactions;

                    public class Module : E9KModuleBase {
                """ + handlers + """

                    }
                }
                """;
            var tree = CSharpSyntaxTree.ParseText(source);
            var refs = new List<MetadataReference>();
            var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
            foreach(var path in trustedAssemblies.Split(Path.PathSeparator))
                refs.Add(MetadataReference.CreateFromFile(path));

            var compilation = CSharpCompilation.Create("Test", [tree], refs.DistinctBy(r => r.Display), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, compileErrors.Count, string.Join("\n", compileErrors));

            var withAnalyzers = compilation.WithAnalyzers([new InteractionAckAnalyzer()]);
            return [.. await withAnalyzers.GetAnalyzerDiagnosticsAsync()];
        }

        private static void AssertHas(Diagnostic[] diagnostics, string id) =>
            Assert.IsTrue(diagnostics.Any(d => d.Id == id), $"expected {id}; got [{string.Join(", ", diagnostics.Select(d => d.Id))}]");

        private static void AssertNone(Diagnostic[] diagnostics, string id) =>
            Assert.IsFalse(diagnostics.Any(d => d.Id == id), $"unexpected {id}: {string.Join("\n", diagnostics.Where(d => d.Id == id).Select(d => d.GetMessage()))}");

        [TestMethod]
        public async Task Component_UpdateAsync_IsDoubleAck() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler() {
                    await Context.Interaction.UpdateAsync(x => { });
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.DoubleAckDiagnosticId);
        }

        [TestMethod]
        public async Task Component_RespondAsync_IsDoubleAck() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler() {
                    await Context.Interaction.RespondAsync("hi", ephemeral: true);
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.DoubleAckDiagnosticId);
        }

        [TestMethod]
        public async Task Component_RespondWithModal_WithoutNoAutoAck_IsDoubleAck() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler() {
                    await Context.Interaction.RespondWithModalAsync(new object());
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.DoubleAckDiagnosticId);
        }

        [TestMethod]
        public async Task Component_RespondWithModal_WithNoAutoAck_Clean() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                [NoAutoAck]
                public async Task Handler() {
                    await Context.Interaction.RespondWithModalAsync(new object());
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.DoubleAckDiagnosticId);
            AssertNone(d, InteractionAckAnalyzer.EditBeforeAckDiagnosticId);
        }

        [TestMethod]
        public async Task Component_Modify_Clean() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler() {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            Assert.AreEqual(0, d.Length, string.Join("\n", d.Select(x => x.GetMessage())));
        }

        [TestMethod]
        public async Task Component_LegacyHasRespondedGuard_IsRedundant() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler() {
                    if(!Context.Interaction.HasResponded) await Context.Interaction.DeferAsync();
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.RedundantDeferDiagnosticId);
            AssertNone(d, InteractionAckAnalyzer.DoubleAckDiagnosticId);
        }

        [TestMethod]
        public async Task Component_DeferDisabling_IsRedundant() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler() {
                    await Context.Interaction.DeferDisablingAsync();
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.RedundantDeferDiagnosticId);
        }

        [TestMethod]
        public async Task Component_BareReturnAfterAck_IsStuck() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler(string data) {
                    if(data is null) return;
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Component_RejectThenReturn_Clean() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler(string data) {
                    if(data is null) { await Context.Interaction.RejectAsync("gone"); return; }
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Component_FallOffEndWithoutEdit_IsStuck() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler(string data) {
                    Console.WriteLine(data);
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Component_EditInOnlyOneBranch_IsStuck() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler(bool flag) {
                    if(flag) await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Component_EditInBothBranches_Clean() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler(bool flag) {
                    if(flag) await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                    else await Context.Interaction.RejectAsync("no");
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Component_SwitchWithDefault_AllEdit_Clean() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler(int n) {
                    switch(n) {
                        case 1: await Context.Interaction.ModifyOriginalResponseAsync(x => { }); return;
                        default: await Context.Interaction.RejectAsync("no"); return;
                    }
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Component_ThrowPath_NotStuck() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler(string data) {
                    if(data is null) throw new InvalidOperationException();
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Component_InvocationInsideLambda_Ignored() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler() {
                    Func<Task> later = () => Context.Interaction.RespondAsync("x");
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.DoubleAckDiagnosticId);
        }

        [TestMethod]
        public async Task Slash_ModifyWithoutDefer_IsEditBeforeAck() {
            var d = await GetDiagnosticsAsync("""
                [SlashCommand("x", "d")]
                public async Task Handler() {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.EditBeforeAckDiagnosticId);
        }

        [TestMethod]
        public async Task Slash_DeferThenModify_Clean() {
            var d = await GetDiagnosticsAsync("""
                [SlashCommand("x", "d")]
                public async Task Handler() {
                    await Context.Interaction.DeferAsync();
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            Assert.AreEqual(0, d.Length, string.Join("\n", d.Select(x => x.GetMessage())));
        }

        [TestMethod]
        public async Task Slash_RespondOnly_Clean() {
            var d = await GetDiagnosticsAsync("""
                [SlashCommand("x", "d")]
                public async Task Handler() {
                    await Context.Interaction.RespondAsync("hi");
                }
                """);
            Assert.AreEqual(0, d.Length, string.Join("\n", d.Select(x => x.GetMessage())));
        }

        [TestMethod]
        public async Task Slash_DeferInOneBranchOnly_ThenModify_IsEditBeforeAck() {
            var d = await GetDiagnosticsAsync("""
                [SlashCommand("x", "d")]
                public async Task Handler(bool flag) {
                    if(flag) await Context.Interaction.DeferAsync();
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.EditBeforeAckDiagnosticId);
        }

        [TestMethod]
        public async Task Slash_BareReturnAfterDefer_NotFlaggedAsStuck() {
            var d = await GetDiagnosticsAsync("""
                [SlashCommand("x", "d")]
                public async Task Handler(string data) {
                    await Context.Interaction.DeferAsync();
                    if(data is null) return;
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Modal_UpdateAsync_Clean() {
            var d = await GetDiagnosticsAsync("""
                [ModalInteraction("x")]
                public async Task Handler() {
                    await Context.Interaction.UpdateAsync(x => { });
                }
                """);
            Assert.AreEqual(0, d.Length, string.Join("\n", d.Select(x => x.GetMessage())));
        }

        [TestMethod]
        public async Task Modal_ModifyWithoutDefer_IsEditBeforeAck() {
            var d = await GetDiagnosticsAsync("""
                [ModalInteraction("x")]
                public async Task Handler() {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertHas(d, InteractionAckAnalyzer.EditBeforeAckDiagnosticId);
        }

        [TestMethod]
        public async Task Component_EditDelegatedToPrivateHelper_Clean() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler(string data) {
                    await Render(data);
                }

                private async Task Render(string data) {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task Slash_DeferDelegatedToPrivateHelper_ThenModify_Clean() {
            var d = await GetDiagnosticsAsync("""
                [SlashCommand("x", "d")]
                public async Task Handler() {
                    await Prepare();
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                }

                private async Task Prepare() {
                    await Context.Interaction.DeferAsync();
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.EditBeforeAckDiagnosticId);
        }

        [TestMethod]
        public async Task Component_RespondAsyncGettingMessage_NotDoubleAck() {
            var d = await GetDiagnosticsAsync("""
                [ComponentInteraction("X")]
                public async Task Handler() {
                    await Context.Interaction.RespondAsyncGettingMessage("x");
                }
                """);
            AssertNone(d, InteractionAckAnalyzer.DoubleAckDiagnosticId);
            AssertNone(d, InteractionAckAnalyzer.StuckReturnDiagnosticId);
        }

        [TestMethod]
        public async Task NonHandlerMethod_Ignored() {
            var d = await GetDiagnosticsAsync("""
                public async Task Helper() {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { });
                    await Context.Interaction.RespondAsync("x");
                }
                """);
            Assert.AreEqual(0, d.Length);
        }

        [TestMethod]
        public async Task ClassLevelNoAutoAck_AppliesToAllHandlers() {
            var source = Scaffold + """

                namespace Test {
                    using Discord.Interactions;
                    using EGG9000.Bot.Interactions;

                    [NoAutoAck]
                    public class Module : E9KModuleBase {
                        [ComponentInteraction("X")]
                        public async Task Handler() {
                            await Context.Interaction.RespondAsync("x");
                        }
                    }
                }
                """;
            var tree = CSharpSyntaxTree.ParseText(source);
            var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).DistinctBy(r => r.Display);
            var compilation = CSharpCompilation.Create("Test", [tree], refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var d = await compilation.WithAnalyzers([new InteractionAckAnalyzer()]).GetAnalyzerDiagnosticsAsync();
            AssertNone([.. d], InteractionAckAnalyzer.DoubleAckDiagnosticId);
        }
    }
}
