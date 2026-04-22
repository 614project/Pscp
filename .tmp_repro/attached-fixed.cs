using System;
using System.Globalization;
using System.IO;
using System.Text;

#nullable enable

namespace Pscp.Generated;

public static class main
{
    private static readonly __PscpStdin stdin = new();
    private static readonly __PscpStdout stdout = new();

    public static void Main()
    {
        Run();
        stdout.flush();
    }

    private static void Run()
    {
        int n = stdin.readInt();
        int k = stdin.readInt();
        int __length0 = n;
        int[] h = new int[__length0];
        for (int __i1 = 0; __i1 < __length0; __i1++)
        {
            h[__i1] = stdin.readInt();
        }
        var low = 0;
        var high = 1000000000;
        var result = 0;
        while ((low <= high))
        {
            var mid = (low + ((high - low) / 2));
            int cnt = default;
            { for (int a = 0; a < n; a++) { var l = ((a > 0) && (System.Math.Abs((h[a] - h[(a - 1)])) > mid)); var r = ((a < (n - 1)) && (System.Math.Abs((h[a] - h[(a + 1)])) > mid)); cnt += ((l || r) ? 1 : 0); } }
            if ((cnt <= k))
            {
                result = mid;
                high = (mid - 1);
            }
            else
            {
                low = (mid + 1);
            }
        }
        stdout.write(result);
    }
}

public sealed class __PscpStdin
{
    private readonly StreamReader _reader = new(Console.OpenStandardInput(), Encoding.UTF8, false, 1 << 16);

    public int readInt() => ReadInt();
    private int SkipTokenSeparators()
    {
        int next;
        while ((next = _reader.Peek()) >= 0 && char.IsWhiteSpace((char)next))
        {
            _reader.Read();
        }

        return next;
    }
    private int ReadInt()
    {
        int next = SkipTokenSeparators();

    #if DEBUG
        if (next < 0)
        {
            throw new EndOfStreamException("Unexpected end of input.");
        }
    #endif

        bool negative = false;
        if (next is '+' or '-')
        {
            negative = next == '-';
            _reader.Read();
            next = _reader.Peek();
        }

    #if DEBUG
        if (next < 0 || !char.IsDigit((char)next))
        {
            throw new FormatException("Expected an integer token.");
        }
    #endif

        long value = 0;
        bool hasDigits = false;
        while ((next = _reader.Peek()) >= 0 && char.IsDigit((char)next))
        {
            hasDigits = true;
            value = (value * 10L) + (_reader.Read() - '0');
        }

    #if DEBUG
        if (!hasDigits)
        {
            throw new FormatException("Expected an integer token.");
        }
    #endif


    #if DEBUG
        if (next >= 0 && !char.IsWhiteSpace((char)next))
        {
            throw new FormatException("Invalid integer token.");
        }

        if ((!negative && value > int.MaxValue) || (negative && value > 2147483648L))
        {
            throw new OverflowException("Integer token is out of range for int.");
        }
    #endif

        return negative ? unchecked((int)(-value)) : unchecked((int)value);
    }
}


public sealed class __PscpStdout
{
    private readonly StreamWriter _writer = new(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16) { AutoFlush = false };

    public void flush() => _writer.Flush();

    public void write(int value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
public void writeln(int value)
{
    write(value);
    _writer.WriteLine();
}
}

