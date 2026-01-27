using System;
using ByteAether.Ulid;

namespace Atlas.Helpers;

/// <summary>
/// Generic ULID generation utilities.
/// </summary>
public static class UlidHelper
{
    /// <summary>
    /// Generates a new ULID as a string.
    /// </summary>
    /// <returns>A new ULID string.</returns>
    public static string NewId() => Ulid.New().ToString();
    
    /// <summary>
    /// Validates if a string is a valid ULID.
    /// </summary>
    /// <param name="ulid">The string to validate.</param>
    /// <returns>True if the string is a valid ULID, false otherwise.</returns>
    public static bool IsValid(string? ulid)
    {
        if (string.IsNullOrWhiteSpace(ulid))
            return false;
            
        try
        {
            Ulid.Parse(ulid);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Parses a string to a ULID.
    /// </summary>
    /// <param name="ulid">The string to parse.</param>
    /// <returns>The parsed ULID.</returns>
    /// <exception cref="FormatException">Thrown when the string is not a valid ULID.</exception>
    public static Ulid Parse(string ulid) => Ulid.Parse(ulid);
    
    /// <summary>
    /// Tries to parse a string to a ULID.
    /// </summary>
    /// <param name="ulid">The string to parse.</param>
    /// <param name="result">The parsed ULID if successful.</param>
    /// <returns>True if parsing was successful, false otherwise.</returns>
    public static bool TryParse(string? ulid, out Ulid result) 
    {
        result = default;
        if (string.IsNullOrWhiteSpace(ulid))
            return false;
            
        try
        {
            result = Ulid.Parse(ulid);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
