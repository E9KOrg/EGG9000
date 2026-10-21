using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Site.Helpers;

public static class HttpRequestQueryExtensions {
    public static string WithQuery(this HttpRequest request, string key, string value, params string[] drop) {
        var query = request.Query
            .Where(x => !string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            .Where(x => !drop.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
            .SelectMany(x => x.Value.Select(v => KeyValuePair.Create(x.Key, v ?? "")))
            .ToList();
        query.Add(KeyValuePair.Create(key, value));
        return new QueryBuilder(query).ToQueryString().Value;
    }
}
