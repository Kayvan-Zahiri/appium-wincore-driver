using System.Text.Json;

namespace WincoreServer.Commands;

/// <summary>
/// Reads and validates the values of a W3C Set Window Rect request (and of the
/// <c>moveWindow</c> / <c>resizeWindow</c> RPCs), so that a bad value fails with
/// <see cref="ArgumentException"/> (→ <c>InvalidArgument</c>) before the window is touched.
/// </summary>
internal static class WindowRect
{
    /// <summary>Smallest allowed x / y: -(2^31).</summary>
    public const double MinCoordinate = int.MinValue;

    /// <summary>Smallest allowed width / height.</summary>
    public const double MinSize = 0;

    /// <summary>
    /// Reads one optional field: missing or <c>null</c> means "keep the current value";
    /// anything else must be a finite JSON number that rounds to an integer from
    /// <paramref name="min"/> to 2^31 - 1.
    /// </summary>
    public static int? ReadOptional(JsonElement parameters, string name, double min)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return ToInt(value, name, min);
    }

    /// <summary>Reads one required field, validated like <see cref="ReadOptional"/>.</summary>
    public static int ReadRequired(JsonElement parameters, string name, double min) =>
        ReadOptional(parameters, name, min) ?? throw new ArgumentException(Message(name, min));

    private static int ToInt(JsonElement value, string name, double min)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
        {
            throw new ArgumentException(Message(name, min));
        }
        // Round half up, like JavaScript's Math.round, and check the rounded value so that
        // e.g. 2^31 - 0.5 can't round up past the limit.
        var rounded = Math.Floor(number + 0.5);
        if (!double.IsFinite(rounded) || rounded < min || rounded > int.MaxValue)
        {
            throw new ArgumentException(Message(name, min));
        }
        return (int)rounded;
    }

    private static string Message(string name, double min) =>
        min >= 0 ? $"{name} must be a non-negative integer." : $"{name} must be an integer.";
}
