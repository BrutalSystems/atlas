using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Atlas.Extensions;

/// <summary>
/// Generic string extension methods.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Checks if a string is null or empty.
    /// </summary>
    /// <param name="input">The string to check.</param>
    /// <returns>True if the string is null or empty, false otherwise.</returns>
    public static bool IsNullOrEmpty(this string? input) => string.IsNullOrEmpty(input);

    /// <summary>
    /// Checks if a string is null, empty, or contains only whitespace characters.
    /// </summary>
    /// <param name="input">The string to check.</param>
    /// <returns>True if the string is null, empty, or whitespace, false otherwise.</returns>
    public static bool IsNullOrWhiteSpace(this string? input) => string.IsNullOrWhiteSpace(input);

    /// <summary>
    /// Checks if a string is not null, not empty, and contains non-whitespace characters.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static bool IsNotBlank(this string? input) => !string.IsNullOrWhiteSpace(input);

    /// <summary>
    /// Converts a string to a URL-friendly slug.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>A URL-friendly slug.</returns>
    public static string ToSlug(this string input)
    {
        if (input.IsNullOrWhiteSpace())
            return string.Empty;

        // Convert to lowercase
        var slug = input.ToLowerInvariant();

        // Remove diacritics (accents)
        slug = RemoveDiacritics(slug);

        // Replace spaces and invalid characters with hyphens
        slug = Regex.Replace(slug, @"[^a-z0-9\-_]", "-");

        // Replace multiple consecutive hyphens with a single hyphen
        slug = Regex.Replace(slug, @"-+", "-");

        // Remove leading and trailing hyphens
        slug = slug.Trim('-');

        return slug;
    }

    /// <summary>
    /// Truncates a string to the specified maximum length.
    /// </summary>
    /// <param name="input">The string to truncate.</param>
    /// <param name="maxLength">The maximum length.</param>
    /// <param name="suffix">Optional suffix to append if truncated (default: "...").</param>
    /// <returns>The truncated string.</returns>
    public static string Truncate(this string input, int maxLength, string suffix = "...")
    {
        if (input.IsNullOrEmpty() || input.Length <= maxLength)
            return input ?? string.Empty;

        var truncateLength = Math.Max(0, maxLength - suffix.Length);
        return input[..truncateLength] + suffix;
    }

    /// <summary>
    /// Removes diacritics (accents) from a string.
    /// </summary>
    /// <param name="input">The string to process.</param>
    /// <returns>The string without diacritics.</returns>
    private static string RemoveDiacritics(string input)
    {
        if (input.IsNullOrEmpty())
            return string.Empty;

        var normalizedString = input.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }
}