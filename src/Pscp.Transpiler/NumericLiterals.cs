using System.Globalization;
using System.Numerics;

namespace Pscp.Transpiler;

// Classifies numeric literal text the same way C# does, so the transpiler, the semantic analyzer, and the
// language server agree on literal types (suffixes, hex/binary prefixes, `_` digit separators).
public static class PscpNumericLiterals
{
    public static string GetTypeName(string rawText, bool isFloat)
    {
        string text = rawText.Replace("_", string.Empty, StringComparison.Ordinal);
        if (text.Length == 0)
        {
            return isFloat ? "double" : "int";
        }

        bool isRadixLiteral = IsRadixLiteral(text);
        if (!isRadixLiteral)
        {
            char last = char.ToLowerInvariant(text[^1]);
            switch (last)
            {
                case 'f':
                    return "float";
                case 'd':
                    return "double";
                case 'm':
                    return "decimal";
            }
        }

        if (isFloat)
        {
            return "double";
        }

        string lower = text.ToLowerInvariant();
        bool unsignedSuffix = lower.EndsWith('u') || lower.EndsWith("ul", StringComparison.Ordinal) || lower.EndsWith("lu", StringComparison.Ordinal);
        bool longSuffix = lower.EndsWith('l') || lower.EndsWith("ul", StringComparison.Ordinal) || lower.EndsWith("lu", StringComparison.Ordinal);
        string digits = lower.TrimEnd('u', 'l');
        BigInteger? value = TryParseIntegerValue(digits);

        if (unsignedSuffix && longSuffix)
        {
            return "ulong";
        }

        if (unsignedSuffix)
        {
            return value is { } unsignedValue && unsignedValue > uint.MaxValue ? "ulong" : "uint";
        }

        if (longSuffix)
        {
            return value is { } longValue && longValue > long.MaxValue ? "ulong" : "long";
        }

        if (value is not { } plain || plain <= int.MaxValue)
        {
            return "int";
        }

        if (plain <= uint.MaxValue)
        {
            return "uint";
        }

        return plain <= long.MaxValue ? "long" : "ulong";
    }

    // True when the literal cannot be represented as an `int` (explicit long suffix or a value above int.MaxValue).
    public static bool IsWiderThanInt(string rawText, bool isFloat)
        => GetTypeName(rawText, isFloat) is "long" or "ulong" or "uint";

    public static bool TryGetInt32Value(string rawText, out int value)
    {
        value = 0;
        string digits = rawText.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant().TrimEnd('u', 'l');
        if (TryParseIntegerValue(digits) is { } parsed && parsed >= int.MinValue && parsed <= int.MaxValue)
        {
            value = (int)parsed;
            return true;
        }

        return false;
    }

    private static bool IsRadixLiteral(string text)
        => text.Length > 1 && text[0] == '0' && text[1] is 'x' or 'X' or 'b' or 'B';

    private static BigInteger? TryParseIntegerValue(string digits)
    {
        if (digits.Length > 2 && digits[0] == '0' && digits[1] == 'x')
        {
            return BigInteger.TryParse("0" + digits[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out BigInteger hex)
                ? hex
                : null;
        }

        if (digits.Length > 2 && digits[0] == '0' && digits[1] == 'b')
        {
            BigInteger result = BigInteger.Zero;
            foreach (char ch in digits[2..])
            {
                if (ch is not '0' and not '1')
                {
                    return null;
                }

                result = (result << 1) + (ch - '0');
            }

            return result;
        }

        return BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger decimalValue)
            ? decimalValue
            : null;
    }
}
