using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EGG9000.Analyzers;

/// <summary>
/// Warns when <c>string.Equals(value, StringComparison)</c> is used inside a LINQ lambda
/// that operates on an <see cref="System.Linq.IQueryable{T}"/> source, because EF Core cannot
/// translate that overload to SQL.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LinqStringEqualsAnalyzer : DiagnosticAnalyzer {
    public const string DiagnosticId = "EGG003";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Avoid string.Equals with StringComparison inside IQueryable LINQ lambdas",
        messageFormat: "'{0}' uses string.Equals with a StringComparison parameter inside an IQueryable LINQ lambda, which EF Core cannot translate to SQL. Use ToLower() == or EF.Functions.Like() instead.",
        category: "EntityFramework",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "EF Core cannot translate string.Equals overloads that accept a StringComparison parameter. " +
                     "Replace with a translatable form such as `s.ToLower() == value.ToLower()` or `EF.Functions.Like(s, value)`.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    // LINQ operator method names that accept lambdas and can be applied to IQueryable.
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
        "Include",  // EF Core navigation includes that accept expression lambdas
        "ThenInclude"
    );

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context) {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Must be a member access: <receiver>.Equals(...)
        if(invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if(memberAccess.Name.Identifier.Text != "Equals")
            return;

        // Must have exactly 2 arguments: (value, StringComparison)
        var args = invocation.ArgumentList.Arguments;
        if(args.Count != 2)
            return;

        // Verify via semantic model that the second argument is System.StringComparison
        var secondArgType = context.SemanticModel.GetTypeInfo(args[1].Expression, context.CancellationToken).Type;
        if(secondArgType?.ToDisplayString() != "System.StringComparison")
            return;

        // Verify via semantic model that the receiver is a string
        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if(receiverType?.SpecialType != SpecialType.System_String)
            return;

        // Walk up the syntax tree to find if this call lives inside a lambda
        // whose parent LINQ method operates on an IQueryable source.
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
                // This lambda must be a direct argument to a LINQ method call.
                if(current.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax outerCall } }) {
                    if(IsQueryableLinqCall(outerCall, semanticModel, cancellationToken))
                        return true;
                }
                // Lambda found but not in a qualifying position — stop walking
                // (nested lambdas would need their own check; keep walking to cover nesting).
            }
            current = current.Parent;
        }

        return false;
    }

    private static bool IsQueryableLinqCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken) {

        // The method name must be a known LINQ operator.
        var methodName = invocation.Expression switch {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => null
        };

        if(methodName is null || !LinqLambdaMethods.Contains(methodName))
            return false;

        // Use the semantic model to confirm the resolved symbol operates on IQueryable<T>.
        var symbolInfo = semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken);
        var candidates = symbolInfo.Symbol is not null
            ? ImmutableArray.Create(symbolInfo.Symbol)
            : symbolInfo.CandidateSymbols;

        foreach(var symbol in candidates) {
            if(symbol is not IMethodSymbol method)
                continue;

            // Extension method: first parameter type must be or implement IQueryable<T>
            if(method.IsExtensionMethod && method.Parameters.Length > 0) {
                if(ImplementsIQueryable(method.Parameters[0].Type))
                    return true;
            }

            // Instance method: containing type must implement IQueryable<T>
            if(ImplementsIQueryable(method.ContainingType))
                return true;
        }

        // Semantic resolution failed or returned nothing — fall back to a syntax-level
        // heuristic: check if the receiver's inferred type implements IQueryable<T>.
        if(invocation.Expression is MemberAccessExpressionSyntax memberAccess) {
            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            if(receiverType is not null && ImplementsIQueryable(receiverType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is, or implements / is assignable to,
    /// <c>System.Linq.IQueryable&lt;T&gt;</c>.
    /// </summary>
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
