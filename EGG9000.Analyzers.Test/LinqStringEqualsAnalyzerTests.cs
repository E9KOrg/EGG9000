using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using EGG9000.Analyzers;

namespace EGG9000.Analyzers.Test;

using Verify = CSharpAnalyzerVerifier<LinqStringEqualsAnalyzer, DefaultVerifier>;

public class LinqStringEqualsAnalyzerTests {

    // -------------------------------------------------------------------------
    // Cases that SHOULD produce a diagnostic
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Equals_WithStringComparison_InWhereLambda_OnIQueryable_Warning() {
        var source = """
            using System;
            using System.Linq;

            public class Test {
                public void Run(IQueryable<string> query, string value) {
                    var result = query.Where(s => s.{|#0:Equals(value, StringComparison.OrdinalIgnoreCase)|});
                }
            }
            """;

        var expected = Verify.Diagnostic(LinqStringEqualsAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Warning);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Equals_WithStringComparison_InJoinLambda_OnIQueryable_Warning() {
        var source = """
            using System;
            using System.Linq;

            public class Coop { public string Name { get; set; } }
            public class User { public string CoopName { get; set; } }

            public class Test {
                public void Run(IQueryable<User> users, IQueryable<Coop> coops, string targetName) {
                    var result = users.Join(
                        coops,
                        u => u.CoopName,
                        c => c.Name,
                        (u, c) => new { u, c })
                        .Where(x => x.c.Name.{|#0:Equals(targetName, StringComparison.CurrentCultureIgnoreCase)|});
                }
            }
            """;

        var expected = Verify.Diagnostic(LinqStringEqualsAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Warning);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Equals_WithStringComparison_InSelectLambda_OnIQueryable_Warning() {
        var source = """
            using System;
            using System.Linq;

            public class Test {
                public void Run(IQueryable<string> query, string value) {
                    var result = query.Select(s => s.{|#0:Equals(value, StringComparison.InvariantCultureIgnoreCase)|});
                }
            }
            """;

        var expected = Verify.Diagnostic(LinqStringEqualsAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Warning);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    // -------------------------------------------------------------------------
    // Cases that should NOT produce a diagnostic
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Equals_WithStringComparison_OnIEnumerable_NoDiagnostic() {
        // IEnumerable is evaluated in-process; no SQL translation required.
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Test {
                public void Run(IEnumerable<string> list, string value) {
                    var result = list.Where(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Equals_WithStringComparison_OutsideLambda_NoDiagnostic() {
        // Plain method body — not inside any LINQ lambda.
        var source = """
            using System;

            public class Test {
                public bool Run(string a, string b) {
                    return a.Equals(b, StringComparison.OrdinalIgnoreCase);
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Equals_OneArgOverload_InIQueryableLambda_NoDiagnostic() {
        // Only the two-argument overload (with StringComparison) is non-translatable.
        var source = """
            using System;
            using System.Linq;

            public class Test {
                public void Run(IQueryable<string> query, string value) {
                    var result = query.Where(s => s.Equals(value));
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NonStringEquals_WithTwoArgs_InIQueryableLambda_NoDiagnostic() {
        // Receiver is not a string — should not flag.
        var source = """
            using System;
            using System.Linq;

            public class MyObj { public bool Equals(object other, StringComparison c) => true; }

            public class Test {
                public void Run(IQueryable<MyObj> query, MyObj value) {
                    var result = query.Where(s => s.Equals(value, StringComparison.Ordinal));
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Equals_WithStringComparison_InAsEnumerableLambda_NoDiagnostic() {
        // Once AsEnumerable() is called, the subsequent LINQ operators run in-process.
        var source = """
            using System;
            using System.Linq;

            public class Test {
                public void Run(IQueryable<string> query, string value) {
                    var result = query.AsEnumerable()
                                      .Where(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
