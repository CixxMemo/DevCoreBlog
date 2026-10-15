using Microsoft.Extensions.Logging;

namespace DevCoreBlog.Configuration;

/// <summary>Framework request diagnostics can print opaque URL proofs before middleware; fixed application events replace those raw diagnostics.</summary>
public static class PrivateLogFilters
{
    private static readonly string[] Categories=["Microsoft.EntityFrameworkCore","Npgsql","Microsoft.AspNetCore.Mvc.ModelBinding",
        "Microsoft.AspNetCore.Hosting.Diagnostics","Microsoft.AspNetCore.Server.Kestrel","Microsoft.AspNetCore.Routing","Microsoft.AspNetCore.HttpLogging"];
    public static void Configure(LoggerFilterOptions options)
    {
        for(var i=options.Rules.Count-1;i>=0;i--)
            if(options.Rules[i].CategoryName is {} name && Categories.Any(prefix=>name.StartsWith(prefix,StringComparison.Ordinal)))options.Rules.RemoveAt(i);
        foreach(var name in Categories)options.Rules.Add(new LoggerFilterRule(null,name,LogLevel.None,null));
    }
}
