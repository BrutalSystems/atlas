using Atlas.Mvc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Atlas.Extensions;

public static class MvcOptionsExtension
{
    public static void UseCentralRoutePrefix(this MvcOptions options, RouteAttribute routeAttribute, string? forRouteTemplate = null)
    {
        options.Conventions.Insert(0, new RouteConvention(routeAttribute, forRouteTemplate));
    }

    public class RouteConvention : IApplicationModelConvention
    {
        private readonly AttributeRouteModel _centralPrefix;
        private readonly string? _forRouteTemplate;

        public RouteConvention(IRouteTemplateProvider routeTemplateProvider, string? routeId)
        {
            _centralPrefix = new AttributeRouteModel(routeTemplateProvider);
            _forRouteTemplate = routeId;
        }

        public void Apply(ApplicationModel application)
        {
            foreach (var controller in application.Controllers)
            {
                var matchedSelectors = controller.Selectors.Where(x => x.AttributeRouteModel != null && (_forRouteTemplate == null || x.AttributeRouteModel.Template == _forRouteTemplate)).ToList();
                if (matchedSelectors.Any())
                {
                    foreach (var selectorModel in matchedSelectors)
                    {
                        selectorModel.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(_centralPrefix,
                            selectorModel.AttributeRouteModel);
                    }
                }
            }
        }
    }

    // ------

    public static void UseEnabledActions(this MvcOptions options)
    {
        options.Conventions.Add(new EnabledActionsConvention());
    }

    public class EnabledActionsConvention : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            foreach (var controller in application.Controllers)
            {
                var attr = controller.Attributes.FirstOrDefault(a => a is EnabledActionsAttribute) as EnabledActionsAttribute;
                var enabledActions = attr?.ActionNames ?? [];
                var enableAll = enabledActions.Contains("*");
                if (enableAll) continue;
                for (var i = controller.Actions.Count - 1; i >= 0; i--)
                {
                    var action = controller.Actions[i];
                    if (EnabledActionsAttribute.ControlledActions.Contains(action.ActionName))
                    {
                        if (enabledActions.Contains(action.ActionName) == false)
                        {
                            controller.Actions.Remove(action);
                        }
                    }
                    else
                    {
                    }
                }
            }
        }
    }

    // -----

    public static void UseRequestCleaner(this MvcOptions options)
    {
        options.Conventions.Add(new RequestCleanerConvention());
    }

    public class RequestCleanerConvention : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            foreach (var controller in application.Controllers)
            {
                var controllerType = controller.ControllerType;
                var isBaseController = controllerType.BaseType != null &&
                                   controllerType.BaseType.Name.StartsWith("BaseController");

                if (isBaseController)
                {
                    var baseType = controllerType.BaseType;
                    if (baseType != null && baseType.IsGenericType)
                    {
                        var genericArguments = baseType.GetGenericArguments();
                        if (genericArguments.Length > 0)
                        {
                            var entityType = genericArguments[0];

                            var properties = entityType.GetProperties();
                            var classProperties = properties
                                .Where(p =>
                                {
                                    var propType = p.PropertyType;

                                    // Check if it's a class (excluding string and primitives)
                                    if (propType == typeof(string) || propType.IsPrimitive)
                                        return false;

                                    // Check if it's a collection/enumerable type
                                    if (propType.IsGenericType)
                                    {
                                        var genericDef = propType.GetGenericTypeDefinition();
                                        if (genericDef == typeof(ICollection<>) ||
                                            genericDef == typeof(IEnumerable<>) ||
                                            genericDef == typeof(List<>) ||
                                            genericDef == typeof(IList<>))
                                            return true;
                                    }

                                    // Check if it implements IEnumerable (for other collection types)
                                    if (typeof(System.Collections.IEnumerable).IsAssignableFrom(propType) &&
                                        propType != typeof(string))
                                        return true;

                                    // Check for navigation properties (single entity references)
                                    // These are typically classes that have an Id property
                                    var hasIdProperty = propType.GetProperty("Id") != null;
                                    return hasIdProperty;
                                })
                                .Select(p => p.Name)
                                .ToList();

                            RequestCleanerMiddleware.AddTarget($"/api/{controller.ControllerName}", classProperties);
                        }
                    }
                }
            }
        }
    }

}