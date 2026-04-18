using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

#nullable enable

namespace Pscp.Generated;

public static class mainProgram
{
    private static readonly __PscpStdin stdin = new();
    private static readonly __PscpStdout stdout = new();

    private static int s = default!;
    private static int e = default!;
    private static int[] b = default!;
    private static int[] k = default!;
    private static List<int>[] graph = default!;
    private static int[] visits = default!;
    private static int bug = default!;

    public static int dfs(int me, int health, int bananas)
    {
        var ret = (-1);
        if ((health == 0))
        {
            if ((me == e))
            {
                return (ret = bananas);
            }
            return ret;
        }
        foreach (var other in graph[me])
        {
            if (((k[other] == 1) || (visits[other] == 2)))
            {
                continue;
            }
            visits[other]++;
            var earned = ((visits[other] == 1) ? b[other] : 0);
            ret = System.Math.Max(ret, dfs(other, (health - 1), (bananas + earned)));
            visits[other]--;
        }
        return ret;
    }

    public static void BugTest()
    {
        __PscpSeq.chmax(ref bug, 1);
    }

    public static void Main()
    {
        Run();
        stdout.flush();
    }

    private static void Run()
    {
        int n = stdin.@int();
        int h = stdin.@int();
        s = stdin.@int();
        e = stdin.@int();
        s--;
        e--;
        int __length0 = n;
        b = new int[__length0];
        for (int __i1 = 0; __i1 < __length0; __i1++)
        {
            b[__i1] = stdin.@int();
        }
        int __length2 = n;
        k = new int[__length2];
        for (int __i3 = 0; __i3 < __length2; __i3++)
        {
            k[__i3] = stdin.@int();
        }
        int __length4 = n;
        graph = new List<int>[__length4];
        for (int __i5 = 0; __i5 < __length4; __i5++)
        {
            graph[__i5] = new List<int>();
        }
        {
            int __start7 = 0;
            int __end8 = (n - 1);
            for (int __item6 = __start7; __item6 < __end8; __item6++)
            {
                {
                    int a = stdin.@int();
                    int b = stdin.@int();
                    void Add(int x, int y)
                    {
                        graph[x].Add(y);
                        graph[y].Add(x);
                    }
                    Add((--a), (--b));
                }
            }
        }
        visits = new int[n];
        bug = 0;
        BugTest();
        visits[s] = 1;
        stdout.writeln(dfs(s, h, b[s]));
    }
}

public static class __PscpSeq
{
    public static bool chmax<T>(ref T target, T value)
    {
        if (Comparer<T>.Default.Compare(value, target) > 0)
        {
            target = value;
            return true;
        }

        return false;
    }

}

public sealed class __PscpStdin
{
    private readonly StreamReader _reader = new(Console.OpenStandardInput(), Encoding.UTF8, false, 1 << 16);

    public int @int() => ReadInt();
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

