using Microsoft.AspNetCore.Mvc;

namespace Atlas.Services.Authorization;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ServicePermissionsAttribute : Attribute
{
    public ServicePermissionsAttribute(params string[] permissions)
    {
        Permissions = [];
        foreach (var actionPermissionValue in permissions)
        {
            var a1 = actionPermissionValue.Split(':');

            if (a1.Length < 2)
            {
                if (a1[0] == "*")
                {
                    // Wildcard to allow all actions without specific permission checks
                    Permissions.Add(new ActionPermission("*", "*", null));
                    continue;
                }
                throw new ArgumentException($"Invalid actionPermissionValue format: '{actionPermissionValue}'. Expected 'Action:Permission[=Value]'");
            }
            var action = a1[0];
            var a2 = a1[1].Split('=');
            var permission = a2[0];
            var value = a2.Length > 1 ? a2[1] : null;

            Permissions.Add(new ActionPermission(action, permission, value));
        }
    }
    
    public readonly List<ActionPermission> Permissions;
}

public class ActionPermission 
{
    public string Action { get; set; }
    public string Permission { get; set; }
    public string? Value { get; set; }

    public ActionPermission(string action, string permission, string? value)
    {
        Action = action;
        Permission = permission;
        Value = value;
    }
}