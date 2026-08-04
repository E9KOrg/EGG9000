using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace EGG9000.Analyzers {
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class BannerCommentAnalyzer : DiagnosticAnalyzer {
        public const string DividerDiagnosticId = "EGG001";
        public const string AlignmentDiagnosticId = "EGG002";

        private static readonly DiagnosticDescriptor DividerRule = new(
            DividerDiagnosticId,
            "Banner or divider comment",
            "Comment is a decorative banner/divider ('{0}') - express structure via naming and method extraction instead",
            "Style",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor AlignmentRule = new(
            AlignmentDiagnosticId,
            "Vertical alignment padding before comment",
            "Comment is padded with extra spaces to align with adjacent lines - use a single space",
            "Style",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DividerRule, AlignmentRule);

        private static readonly Regex DividerPattern = new(@"^[-=*#~_]{3,}$", RegexOptions.Compiled);
        private static readonly Regex BoxDrawingPattern = new(@"[─-╿]", RegexOptions.Compiled);

        public override void Initialize(AnalysisContext context) {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxTreeAction(AnalyzeTrivia);
        }

        private static void AnalyzeTrivia(SyntaxTreeAnalysisContext context) {
            var root = context.Tree.GetRoot(context.CancellationToken);
            foreach(var trivia in root.DescendantTrivia()) {
                if(trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) {
                    CheckDivider(context, trivia);
                    CheckAlignment(context, trivia);
                }
            }
        }

        private static void CheckDivider(SyntaxTreeAnalysisContext context, SyntaxTrivia trivia) {
            var text = trivia.ToString();
            var stripped = StripCommentMarkers(text).Trim();
            if(stripped.Length == 0) return;

            if(DividerPattern.IsMatch(stripped) || BoxDrawingPattern.IsMatch(stripped)) {
                context.ReportDiagnostic(Diagnostic.Create(DividerRule, trivia.GetLocation(), Truncate(stripped, 30)));
            }
        }

        private static void CheckAlignment(SyntaxTreeAnalysisContext context, SyntaxTrivia trivia) {
            if(!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) return;

            var line = trivia.SyntaxTree.GetText(context.CancellationToken).Lines.GetLineFromPosition(trivia.SpanStart);
            var lineText = line.ToString();
            var commentStart = trivia.SpanStart - line.Start;
            if(commentStart <= 0) return;

            var before = lineText.Substring(0, commentStart);
            var trimmedBefore = before.TrimEnd(' ');
            var codeBeforeComment = trimmedBefore.TrimStart();
            if(codeBeforeComment.Length == 0) return;

            var paddingSpaces = before.Length - trimmedBefore.Length;
            if(paddingSpaces >= 2) {
                context.ReportDiagnostic(Diagnostic.Create(AlignmentRule, trivia.GetLocation()));
            }
        }

        private static string StripCommentMarkers(string text) {
            if(text.StartsWith("///")) return text.Substring(3);
            if(text.StartsWith("//")) return text.Substring(2);
            if(text.StartsWith("/*")) {
                var inner = text.Substring(2);
                if(inner.EndsWith("*/")) inner = inner.Substring(0, inner.Length - 2);
                return inner;
            }
            return text;
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }
}
