using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EGG9000.Analyzers {
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LinqStringEqualsAnalyzer : DiagnosticAnalyzer {
        public const string DiagnosticId = "EGG003";

        private static readonly DiagnosticDescriptor Rule = new(
            id: DiagnosticId,
            title: "Avoid string.Equals with StringComparison inside IQueryable LINQ lambdas",
            messageFormat: "'{0}' uses string.Equals with a StringComparison parameter inside an IQueryable LINQ lambda, which EF Core cannot translate to SQL. Use ToLower() == or EF.Functions.ILike() instead.",
            category: "EntityFramework",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "EF Core cannot translate string.Equals overloads that accept a StringComparison parameter. " +
                         "Replace with a translatable form such as `s.ToLower() == value.ToLower()`, or `EF.Functions.ILike(s, value)` for case-insensitive matching on Postgres (EF.Functions.Like is case-sensitive under Npgsql's default collation).");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        private static readonly ImmutableHashSet<string> LinqLambdaMethods = ImmutableHashSet.Create(
            "Where", "Select", "SelectMany",
            "Join", "GroupJoin",
            "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
            "GroupBy",
            "Any", "All",
            "First", "FirstOrDefault", "Last", "LastOrDefault",
            "Single", "SingleOrDefault",
            "Count", "LongCount",
            "Sum", "Min", "Max", "Average",
            "TakeWhile", "SkipWhile",
            "Include",
            "ThenInclude"
        );

        public override void Initialize(AnalysisContext context) {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context) {
            var invocation = (InvocationExpressionSyntax)context.Node;

            if(invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return;

            if(memberAccess.Name.Identifier.Text != "Equals")
                return;

            var args = invocation.ArgumentList.Arguments;
            if(args.Count != 2)
                return;

            var secondArgType = context.SemanticModel.GetTypeInfo(args[1].Expression, context.CancellationToken).Type;
            if(secondArgType?.ToDisplayString() != "System.StringComparison")
                return;

            var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
            if(receiverType?.SpecialType != SpecialType.System_String)
                return;

            if(!IsInsideQueryableLambda(invocation, context.SemanticModel, context.CancellationToken))
                return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), invocation.ToString()));
        }

        private static bool IsInsideQueryableLambda(
            SyntaxNode node,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken) {

            var current = node.Parent;

            while(current is not null) {
                if(current is LambdaExpressionSyntax) {
                    if(current.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax outerCall } }) {
                        if(IsQueryableLinqCall(outerCall, semanticModel, cancellationToken))
                            return true;
                    }
                }
                current = current.Parent;
            }

            return false;
        }

        private static bool IsQueryableLinqCall(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken) {

            var methodName = invocation.Expression switch {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                _ => null
            };

            if(methodName is null || !LinqLambdaMethods.Contains(methodName))
                return false;

            var symbolInfo = semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken);
            var candidates = symbolInfo.Symbol is not null
                ? ImmutableArray.Create(symbolInfo.Symbol)
                : symbolInfo.CandidateSymbols;

            foreach(var symbol in candidates) {
                if(symbol is not IMethodSymbol method)
                    continue;

                if(method.IsExtensionMethod && method.Parameters.Length > 0) {
                    if(ImplementsIQueryable(method.Parameters[0].Type))
                        return true;
                }

                if(ImplementsIQueryable(method.ContainingType))
                    return true;
            }

            if(invocation.Expression is MemberAccessExpressionSyntax memberAccess) {
                var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
                if(receiverType is not null && ImplementsIQueryable(receiverType))
                    return true;
            }

            return false;
        }

        private static bool ImplementsIQueryable(ITypeSymbol type) {
            if(IsIQueryableType(type))
                return true;

            foreach(var iface in type.AllInterfaces) {
                if(IsIQueryableType(iface))
                    return true;
            }

            return false;
        }

        private static bool IsIQueryableType(ITypeSymbol type) {
            var display = type.OriginalDefinition.ToDisplayString();
            return display is "System.Linq.IQueryable<T>" or "System.Linq.IQueryable";
        }
    }
}
