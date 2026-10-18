using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace EGG9000.Site;

public static class CspReports {
    public const string Path = "/csp-report";
    const int MaxBodyChars = 8192;

    static readonly string[] ExtensionSchemes = ["moz-extension", "chrome-extension", "safari-extension", "safari-web-extension"];

    public static IEndpointConventionBuilder MapCspReports(this IEndpointRouteBuilder app) =>
        app.MapPost(Path, HandleAsync).AllowAnonymous().DisableAntiforgery();

    static async Task<IResult> HandleAsync(HttpRequest request, ILoggerFactory loggers) {
        using var reader = new StreamReader(request.Body);
        var buffer = new char[MaxBodyChars];
        var body = new string(buffer, 0, await reader.ReadBlockAsync(buffer));

        if (FromExtension(body)) return Results.NoContent();

        loggers.CreateLogger(nameof(CspReports)).LogWarning("CSP report-only violation: {Report}", body);
        return Results.NoContent();
    }

    static bool FromExtension(string body) {
        try {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("csp-report", out var report)) return false;
            return report.TryGetProperty("source-file", out var source)
                && source.GetString() is { } file
                && Array.Exists(ExtensionSchemes, s => file.StartsWith(s + ":", StringComparison.OrdinalIgnoreCase));
        } catch (JsonException) {
            return false;
        }
    }
}
