namespace Atlas.Mvc;

[AttributeUsage(AttributeTargets.Class)]
public class EnabledActionsAttribute(params string[] actionNames) : Attribute
{
    public static string[] ControlledActions { get; } =
    [
        "Read",
        "ReadAll",
        "ReadOne",
        "Create",
        "Update",
        "Delete"
    ];

    public string[] ActionNames { get; } = actionNames;
}