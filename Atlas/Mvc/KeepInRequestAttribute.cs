namespace Atlas.Mvc;

/// <summary>
/// Opts an entity property out of <see cref="RequestCleanerMiddleware"/>, which otherwise strips every
/// collection and Id-bearing class property from POST/PUT bodies on the assumption it's an EF
/// navigation. Use it for value data that merely looks like one — e.g. a List of owned values
/// stored as a JSON column — so clients can still write it.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class KeepInRequestAttribute : Attribute { }
