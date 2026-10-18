using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.IO;

namespace EGG9000.Site;

public static class CspReports {
    public const string Path = "/csp-report";

    public static IEndpointConventionBuilder MapCspReports(this IEndpointRouteBuilder app) =>
        app.MapPost(Path, async (HttpRequest request, ILoggerFactory loggers) => {
            var buffer = new char[8192];
            var read = await new StreamReader(request.Body).ReadBlockAsync(buffer);
            loggers.CreateLogger(nameof(CspReports)).LogWarning("CSP report-only violation: {Report}", new string(buffer, 0, read));
            return Results.NoContent();
        }).AllowAnonymous().DisableAntiforgery();
}
