using DevCoreBlog.Configuration;
using DevCoreBlog.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace DevCoreBlog.Routing;

/// <summary>Routes both login methods exclusively through the validated private configuration.</summary>
public sealed class AdminLoginRouteConvention(AdminLoginOptions login) : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (controller.ControllerType != typeof(AccountController)) return;
        foreach (var action in controller.Actions.Where(action => action.ActionName == nameof(AccountController.Login)))
            foreach (var selector in action.Selectors)
                selector.AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(login.Path));
    }
}
