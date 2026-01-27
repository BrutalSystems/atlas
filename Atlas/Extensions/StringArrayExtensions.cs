using System;

namespace Atlas.Extensions;

/// <summary>
/// Extension methods for string arrays.
/// </summary>
public static class StringArrayExtensions
{
    /// <summary>
    /// Safely gets an item from a string array at the specified index.
    /// </summary>
    /// <param name="stringArray">The string array to access.</param>
    /// <param name="index">The index of the item to retrieve.</param>
    /// <returns>The string at the specified index, or null if the index is out of bounds.</returns>
    public static string? TryGet(this string[]? stringArray, int index)
    {
        if (stringArray == null || index < 0 || index >= stringArray.Length)
        {
            return null;
        }

        return stringArray[index];
    }
}
