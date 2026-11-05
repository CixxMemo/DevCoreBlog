using DevCoreBlog.Core.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DevCoreBlog.Infrastructure;

/// <summary>Answers a shelved email path before model binding, so the request reaches no service or store.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class EmailPathClosedAttribute(string view = "EmailPathClosed") : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!EmailPaths.Suspended) return;
        context.Result = new ViewResult { ViewName = view, StatusCode = StatusCodes.Status410Gone };
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
