using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace EGG9000.Analyzers {
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class InteractionAckAnalyzer : DiagnosticAnalyzer {
        public const string DoubleAckDiagnosticId = "EGG004";
        public const string EditBeforeAckDiagnosticId = "EGG005";
        public const string StuckReturnDiagnosticId = "EGG006";
        public const string RedundantDeferDiagnosticId = "EGG007";

        private static readonly DiagnosticDescriptor DoubleAckRule = new(
            DoubleAckDiagnosticId,
            "Component handler acknowledges an already-acknowledged interaction",
            "'{0}' is called in a component handler that E9KModuleBase already acknowledged; use ModifyOriginalResponseAsync, or mark the handler [NoAutoAck] if it must answer the click itself",
            "Discord",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor EditBeforeAckRule = new(
            EditBeforeAckDiagnosticId,
            "Interaction edited before it was acknowledged",
            "'{0}' edits the original response, but no acknowledgement (RespondAsync, DeferAsync, UpdateAsync, RespondWithModalAsync) is guaranteed on this path first",
            "Discord",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor StuckReturnRule = new(
            StuckReturnDiagnosticId,
            "Component handler returns after acknowledgement without editing the message",
            "This path acknowledged the interaction (controls are disabled) but returns without ModifyOriginalResponseAsync, RejectAsync or RestoreComponentsAsync, leaving the controls stuck",
            "Discord",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor RedundantDeferRule = new(
            RedundantDeferDiagnosticId,
            "Redundant acknowledgement guard",
            "'{0}' is redundant: E9KModuleBase acknowledges component interactions before the handler runs",
            "Discord",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DoubleAckRule, EditBeforeAckRule, StuckReturnRule, RedundantDeferRule];

        private static readonly ImmutableHashSet<string> FlaggedAckMethods = ImmutableHashSet.Create(
            "RespondAsync", "RespondWithFilesAsync", "RespondWithModalAsync", "RespondWithPremiumRequiredAsync",
            "DeferAsync", "DeferLoadingAsync", "UpdateAsync", "DeferDisablingAsync");

        private static readonly ImmutableHashSet<string> AckMethods = FlaggedAckMethods.Union(
            ["RespondAsyncGettingMessage", "RespondWithFilesAsyncGettingMessage", "SendAsync"]);

        private static readonly ImmutableHashSet<string> EditsRequiringAck = ImmutableHashSet.Create(
            "ModifyOriginalResponseAsync", "FollowupAsync", "FollowupWithFileAsync", "FollowupWithFilesAsync");

        private static readonly ImmutableHashSet<string> TerminalEditMethods = ImmutableHashSet.Create(
            "ModifyOriginalResponseAsync", "FollowupAsync", "FollowupWithFileAsync", "FollowupWithFilesAsync",
            "RejectAsync", "RestoreComponentsAsync", "UpdateComponentAsync", "RejectNonInvokerAsync",
            "DeleteOriginalResponseAsync", "DeleteResponseFix",
            "RespondAsync", "RespondWithFilesAsync", "RespondWithModalAsync", "UpdateAsync",
            "RespondAsyncGettingMessage", "RespondWithFilesAsyncGettingMessage", "SendAsync");

        private const string ModuleBaseName = "E9KModuleBase";
        private const string NoAutoAckName = "NoAutoAckAttribute";

        private enum HandlerKind { None, Component, Slash, Modal }

        public override void Initialize(AnalysisContext context) {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        private static void AnalyzeMethod(SyntaxNodeAnalysisContext context) {
            var method = (MethodDeclarationSyntax)context.Node;
            if(method.Body is null && method.ExpressionBody is null) return;

            var symbol = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken);
            if(symbol is null || !DerivesFromModuleBase(symbol.ContainingType)) return;

            var kind = HandlerKindOf(symbol);
            if(kind == HandlerKind.None) return;

            var noAutoAck = HasAttribute(symbol, NoAutoAckName) || HasAttribute(symbol.ContainingType, NoAutoAckName);
            var ackedByBase = kind == HandlerKind.Component && !noAutoAck;

            if(ackedByBase) {
                foreach(var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                    if(IsInsideNestedFunction(invocation, method)) continue;
                    var name = MethodName(invocation);
                    if(name is null || !FlaggedAckMethods.Contains(name)) continue;
                    var rule = IsRedundantGuard(invocation, name) ? RedundantDeferRule : DoubleAckRule;
                    context.ReportDiagnostic(Diagnostic.Create(rule, invocation.GetLocation(), name));
                }
            }

            if(method.Body is null) return;

            var walker = new PathWalker(ackedByBase, context.SemanticModel, context.CancellationToken);
            walker.Visit(method.Body);

            foreach(var (location, name) in walker.EditsBeforeAck)
                context.ReportDiagnostic(Diagnostic.Create(EditBeforeAckRule, location, name));

            if(kind == HandlerKind.Component) {
                foreach(var location in walker.StuckReturns)
                    context.ReportDiagnostic(Diagnostic.Create(StuckReturnRule, location));
            }
        }

        private static bool IsRedundantGuard(InvocationExpressionSyntax invocation, string name) {
            if(name == "DeferDisablingAsync") return true;
            if(name != "DeferAsync") return false;
            var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
            return statement?.Parent is IfStatementSyntax ifStatement && ifStatement.Condition.ToString().Contains("HasResponded");
        }

        private sealed class PathWalker(bool startAcked, SemanticModel semanticModel, CancellationToken cancellationToken) {
            public readonly List<(Location Location, string Name)> EditsBeforeAck = [];
            public readonly List<Location> StuckReturns = [];

            private readonly bool _startAcked = startAcked;
            private readonly SemanticModel _semanticModel = semanticModel;
            private readonly CancellationToken _cancellationToken = cancellationToken;

            public void Visit(BlockSyntax body) {
                var end = VisitStatements(body.Statements, new PathState(_startAcked, false));
                if(end is not null && _startAcked && !end.Edited)
                    StuckReturns.Add(body.CloseBraceToken.GetLocation());
            }

            private PathState VisitStatements(SyntaxList<StatementSyntax> statements, PathState state) {
                var current = state;
                foreach(var statement in statements) {
                    current = VisitStatement(statement, current);
                    if(current is null) return null;
                }
                return current;
            }

            private PathState VisitStatement(StatementSyntax statement, PathState state) {
                switch(statement) {
                    case BlockSyntax block:
                        return VisitStatements(block.Statements, state);

                    case ReturnStatementSyntax ret: {
                            var end = ret.Expression is null ? state : ScanExpression(ret.Expression, state);
                            if(_startAcked && !end.Edited) StuckReturns.Add(ret.GetLocation());
                            return null;
                        }

                    case ThrowStatementSyntax:
                        return null;

                    case IfStatementSyntax ifStatement: {
                            var afterCondition = ScanExpression(ifStatement.Condition, state);
                            var thenEnd = VisitStatement(ifStatement.Statement, afterCondition);
                            var elseEnd = ifStatement.Else is null ? afterCondition : VisitStatement(ifStatement.Else.Statement, afterCondition);
                            return Merge(thenEnd, elseEnd);
                        }

                    case SwitchStatementSyntax switchStatement: {
                            var afterExpr = ScanExpression(switchStatement.Expression, state);
                            PathState merged = null;
                            var hasDefault = false;
                            foreach(var section in switchStatement.Sections) {
                                if(section.Labels.Any(l => l is DefaultSwitchLabelSyntax)) hasDefault = true;
                                merged = Merge(merged, VisitStatements(section.Statements, afterExpr));
                            }
                            return hasDefault ? merged : Merge(merged, afterExpr);
                        }

                    case TryStatementSyntax tryStatement: {
                            var merged = VisitStatement(tryStatement.Block, state);
                            foreach(var c in tryStatement.Catches)
                                merged = Merge(merged, VisitStatement(c.Block, state));
                            if(tryStatement.Finally is not null && merged is not null)
                                merged = VisitStatement(tryStatement.Finally.Block, merged);
                            return merged;
                        }

                    case ForEachStatementSyntax forEach:
                        return LoopBody(forEach.Statement, ScanExpression(forEach.Expression, state));
                    case ForStatementSyntax forStatement:
                        return LoopBody(forStatement.Statement, state);
                    case WhileStatementSyntax whileStatement:
                        return LoopBody(whileStatement.Statement, ScanExpression(whileStatement.Condition, state));
                    case DoStatementSyntax doStatement:
                        return LoopBody(doStatement.Statement, state);

                    case UsingStatementSyntax usingStatement:
                        return VisitStatement(usingStatement.Statement, state);
                    case LockStatementSyntax lockStatement:
                        return VisitStatement(lockStatement.Statement, state);
                    case CheckedStatementSyntax checkedStatement:
                        return VisitStatement(checkedStatement.Block, state);
                    case LocalFunctionStatementSyntax:
                        return state;

                    default:
                        return ScanExpression(statement, state);
                }
            }

            private PathState LoopBody(StatementSyntax body, PathState state) => Merge(state, VisitStatement(body, state));

            private static PathState Merge(PathState a, PathState b) {
                if(a is null) return b;
                if(b is null) return a;
                return new PathState(a.Acked && b.Acked, a.Edited && b.Edited);
            }

            private PathState ScanExpression(SyntaxNode node, PathState state) {
                var acked = state.Acked;
                var edited = state.Edited;
                foreach(var invocation in node.DescendantNodesAndSelf(DescendInto).OfType<InvocationExpressionSyntax>()) {
                    var name = MethodName(invocation);
                    if(name is null) continue;

                    if(AckMethods.Contains(name)) {
                        acked = true;
                        if(TerminalEditMethods.Contains(name)) edited = true;
                        continue;
                    }

                    if(TerminalEditMethods.Contains(name)) {
                        if(!acked && EditsRequiringAck.Contains(name))
                            EditsBeforeAck.Add((invocation.GetLocation(), name));
                        edited = true;
                        continue;
                    }

                    var (helperAcks, helperEdits) = HelperEffects(invocation);
                    if(helperAcks) acked = true;
                    if(helperEdits) edited = true;
                }
                return new PathState(acked, edited);
            }

            private (bool Acks, bool Edits) HelperEffects(InvocationExpressionSyntax invocation) {
                if(_semanticModel.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol target) return (false, false);
                if(target.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(_cancellationToken) is not MethodDeclarationSyntax syntax) return (false, false);
                var acks = false;
                var edits = false;
                foreach(var inner in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                    var name = MethodName(inner);
                    if(name is null) continue;
                    if(AckMethods.Contains(name)) acks = true;
                    if(TerminalEditMethods.Contains(name)) edits = true;
                }
                return (acks, edits);
            }

            private static bool DescendInto(SyntaxNode node) =>
                node is not LambdaExpressionSyntax and not AnonymousMethodExpressionSyntax and not LocalFunctionStatementSyntax;
        }

        private sealed class PathState(bool acked, bool edited) {
            public bool Acked { get; } = acked;
            public bool Edited { get; } = edited;
        }

        private static HandlerKind HandlerKindOf(IMethodSymbol method) {
            foreach(var attribute in method.GetAttributes()) {
                switch(attribute.AttributeClass?.Name) {
                    case "ComponentInteractionAttribute": return HandlerKind.Component;
                    case "SlashCommandAttribute": return HandlerKind.Slash;
                    case "ModalInteractionAttribute": return HandlerKind.Modal;
                }
            }
            return HandlerKind.None;
        }

        private static bool DerivesFromModuleBase(INamedTypeSymbol type) {
            for(var current = type; current is not null; current = current.BaseType)
                if(current.Name == ModuleBaseName) return true;
            return false;
        }

        private static bool HasAttribute(ISymbol symbol, string attributeName) =>
            symbol.GetAttributes().Any(a => a.AttributeClass?.Name == attributeName);

        private static string MethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };

        private static bool IsInsideNestedFunction(SyntaxNode node, SyntaxNode stopAt) {
            for(var current = node.Parent; current is not null && current != stopAt; current = current.Parent)
                if(current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax) return true;
            return false;
        }
    }
}
