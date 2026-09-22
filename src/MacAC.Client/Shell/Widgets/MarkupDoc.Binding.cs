using System.Globalization;
using System.Reflection;

namespace MacAC.Client.Shell;

public static partial class MarkupDoc
{
    private static Func<uint> AttachUintLiteralOrMapping(
        string expression, object mapping, string ctx)
    {
        if (!IsMapping(expression))
        {
            uint literal = DecodeUintLiteral(expression, ctx);
            return () => literal;
        }
        PropertyInfo? prop = mapping.GetType().GetProperty(expression[1..^1]) ?? throw new FormatException(
                $"{expression} didn't resolve to a property on "
                + mapping.GetType().Name + $" ({ctx})");
        return () => prop.GetValue(mapping) switch
        {
            uint u => u,
            null => 0u,
            var v => ToUintOrZero(v),
        };
    }

    private static Func<string?> AttachString(string? attr, object mapping)
    {
        if (attr is null)
            return static () => null;
        if (!IsMapping(attr))
            return () => attr;

        string label = attr[1..^1];
        var prop = mapping.GetType().GetProperty(label);
        return prop is null ? (() => attr) : (() => prop.GetValue(mapping)?.ToString());
    }

    private static Action? AttachAct(string? attr, object mapping)
    {
        if (attr is null || !IsMapping(attr))
            return null;

        string label = attr[1..^1];
        var prop = mapping.GetType().GetProperty(label);
        return prop is null || !typeof(Action).IsAssignableFrom(prop.PropertyType)
            ? null
            : (() => (prop.GetValue(mapping) as Action)?.Invoke());
    }

    private static Action<float>? AttachFloatAct(
        string? attr,
        object mapping)
    {
        if (attr is null || !IsMapping(attr))
            return null;

        string label = attr[1..^1];
        var prop = mapping.GetType().GetProperty(label);
        return prop is null
            || !typeof(Action<float>).IsAssignableFrom(prop.PropertyType)
            ? null
            : (val => (prop.GetValue(mapping) as Action<float>)?.Invoke(val));
    }

    private static Action<string>? AttachStringAct(
        string? attr,
        object mapping)
    {
        if (attr is null || !IsMapping(attr))
            return null;

        string label = attr[1..^1];
        var prop = mapping.GetType().GetProperty(label);
        return prop is null
            || !typeof(Action<string>).IsAssignableFrom(prop.PropertyType)
            ? null
            : (val => (prop.GetValue(mapping) as Action<string>)?.Invoke(val));
    }

    private static Action<int>? AttachIntAct(string? attr, object mapping)
    {
        if (attr is null || !IsMapping(attr))
            return null;
        var prop = mapping.GetType().GetProperty(attr[1..^1]);
        return prop is null
            || !typeof(Action<int>).IsAssignableFrom(prop.PropertyType)
            ? null
            : (val => (prop.GetValue(mapping) as Action<int>)?.Invoke(val));
    }

    private static Func<IReadOnlyList<string>> AttachStringRoster(
        string? expression,
        object mapping,
        string ctx)
    {
        if (string.IsNullOrWhiteSpace(expression) || !IsMapping(expression))
            throw new FormatException($"{ctx} has to be a string-list binding");
        var prop = mapping.GetType().GetProperty(expression[1..^1]);
        return prop is null
            || !typeof(IEnumerable<string>).IsAssignableFrom(prop.PropertyType)
            ? throw new FormatException(
                $"{expression} didn't resolve to an IEnumerable<string> property on "
                + mapping.GetType().Name)
            : (() => prop.GetValue(mapping) is IEnumerable<string> vals
            ? vals.ToArray()
            : []);
    }

    private static Func<IReadOnlyList<uint>> AttachUintRoster(
        string? expression,
        object mapping,
        string ctx)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return static () => Array.Empty<uint>();
        if (!IsMapping(expression))
            throw new FormatException($"{ctx} has to be a uint-list binding");
        PropertyInfo? prop = mapping.GetType().GetProperty(expression[1..^1]) ?? throw new FormatException(
                $"{expression} didn't resolve to an IEnumerable<uint> or "
                + "IEnumerable<int> property on " + mapping.GetType().Name);
        if (typeof(IEnumerable<uint>).IsAssignableFrom(prop.PropertyType))
        {
            return () => prop.GetValue(mapping) is IEnumerable<uint> vals
                ? vals.ToArray()
                : [];
        }
        return typeof(IEnumerable<int>).IsAssignableFrom(prop.PropertyType)
            ? (() => prop.GetValue(mapping) is IEnumerable<int> vals
                ? vals.Select(static v => v < 0 ? 0u : (uint)v).ToArray()
                : [])
            : throw new FormatException(
            $"{expression} didn't resolve to an IEnumerable<uint> or "
            + "IEnumerable<int> property on " + mapping.GetType().Name);
    }

    private static Func<IReadOnlyList<bool>> AttachBoolRoster(
        string? expression, object mapping, string ctx)
    {
        if (string.IsNullOrWhiteSpace(expression) || !IsMapping(expression))
            throw new FormatException($"{ctx} has to be a bool-list binding");
        var prop = mapping.GetType().GetProperty(expression[1..^1]);
        return prop is null
            || !typeof(IEnumerable<bool>).IsAssignableFrom(prop.PropertyType)
            ? throw new FormatException(
                $"{expression} didn't resolve to an IEnumerable<bool> property on "
                + mapping.GetType().Name + $" ({ctx})")
            : (() => prop.GetValue(mapping) is IEnumerable<bool> vals
            ? vals.ToArray()
            : []);
    }

    private static Func<IReadOnlyList<uint>> AttachNeededUintRoster(
        string? expression, object mapping, string ctx)
    {
        return string.IsNullOrWhiteSpace(expression)
            ? throw new FormatException($"{ctx} has to be a uint-list binding")
            : AttachUintRoster(expression, mapping, ctx);
    }

    private static Action<int> AttachNeededIntAct(
        string? attr, object mapping, string ctx)
    {
        if (attr is null || !IsMapping(attr))
            throw new FormatException($"{ctx} has to be an Action<int> binding");
        var prop = mapping.GetType().GetProperty(attr[1..^1]);
        return prop is null || !typeof(Action<int>).IsAssignableFrom(prop.PropertyType)
            ? throw new FormatException(
                $"{attr} didn't resolve to an Action<int> property on "
                + mapping.GetType().Name + $" ({ctx})")
            : (val => (prop.GetValue(mapping) as Action<int>)?.Invoke(val));
    }

    private static void AttachBool(
        string? expression,
        object mapping,
        Action<bool> setLiteral,
        Action<Func<bool>> setSrc)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return;
        if (!IsMapping(expression))
        {
            if (bool.TryParse(expression, out bool literal))
                setLiteral(literal);
            return;
        }

        var prop = mapping.GetType().GetProperty(expression[1..^1]);
        if (prop is null || prop.PropertyType != typeof(bool))
        {
            throw new FormatException(
                $"{expression} didn't resolve to a bool property on "
                + mapping.GetType().Name);
        }
        setSrc(() => prop.GetValue(mapping) is true);
    }

    private static Func<bool> AttachNeededBoolReader(
        string? expression,
        object mapping,
        string ctx)
    {
        if (string.IsNullOrWhiteSpace(expression) || !IsMapping(expression))
            throw new FormatException($"{ctx} has to be a bool binding");

        var prop = mapping.GetType().GetProperty(expression[1..^1]);
        return prop is null || prop.PropertyType != typeof(bool)
            ? throw new FormatException(
                $"{expression} didn't resolve to a bool property on "
                + mapping.GetType().Name)
            : (() => prop.GetValue(mapping) is true);
    }

    private static Func<int> AttachNeededIntReader(
        string? expression,
        object mapping,
        string ctx)
    {
        if (string.IsNullOrWhiteSpace(expression) || !IsMapping(expression))
            throw new FormatException($"{ctx} has to be an int binding");
        var prop = mapping.GetType().GetProperty(expression[1..^1]);
        return prop is null || prop.PropertyType != typeof(int)
            ? throw new FormatException(
                $"{expression} didn't resolve to an int property on "
                + mapping.GetType().Name)
            : (() => prop.GetValue(mapping) is int val ? val : -1);
    }

    private static Func<float?> AttachFloat(string? expr, object mapping)
    {
        PropertyInfo? info = Prop(expr, mapping);
        return info is null
            ? (() => 0f)
            : (() => info.GetValue(mapping) switch
        {
            float f => f,
            null => (float?)null,
            var v => Convert.ToSingle(v, CultureInfo.InvariantCulture),
        });
    }

    private static Func<uint?> AttachUint(string? expr, object mapping)
    {
        PropertyInfo? info = Prop(expr, mapping);
        return info is null
            ? (() => null)
            : (() => info.GetValue(mapping) switch
        {
            uint u => u,
            null => (uint?)null,
            var v => Convert.ToUInt32(v, CultureInfo.InvariantCulture),
        });
    }
}
