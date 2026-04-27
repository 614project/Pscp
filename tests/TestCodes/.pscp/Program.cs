using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

#nullable enable

namespace Pscp.Generated;

public static class _04_scc_condensation_sourcesProgram
{
    private static readonly __PscpStdin stdin = new();
    private static readonly __PscpStdout stdout = new();

    public static void solve()
    {
        int n = stdin.readInt();
        int m = stdin.readInt();
        int __length0 = n;
        List<int>[] graph = new List<int>[__length0];
        for (int __i1 = 0; __i1 < __length0; __i1++)
        {
            graph[__i1] = new List<int>();
        }
        int __length2 = n;
        List<int>[] reverseGraph = new List<int>[__length2];
        for (int __i3 = 0; __i3 < __length2; __i3++)
        {
            reverseGraph[__i3] = new List<int>();
        }
        for (int __item4 = 0; __item4 < m; __item4++)
        {
            {
                int a = stdin.readInt();
                int b = stdin.readInt();
                var u = (a - 1);
                var v = (b - 1);
                graph[u].Add(v);
                reverseGraph[v].Add(u);
            }
        }
        bool[] visited = new bool[n];
        List<int> order = new();
        void dfsForward(int v)
        {
            visited[v] = true;
            foreach (var to in graph[v])
            {
                if ((!visited[to]))
                {
                    dfsForward(to);
                }
            }
            order.Add(v);
        }
        for (int __item5 = 0; __item5 < n; __item5++)
        {
            {
                if ((!visited[__item5]))
                {
                    dfsForward(__item5);
                }
            }
        }
        int[] comp = new int[n];
        for (int i = 0; i < n; i++)
        {
            {
                comp[i] = (-1);
            }
        }
        void dfsBackward(int v, int id)
        {
            comp[v] = id;
            foreach (var to in reverseGraph[v])
            {
                if ((comp[to] == (-1)))
                {
                    dfsBackward(to, id);
                }
            }
        }
        var compCount = 0;
        foreach (var __item6 in order.reverse())
        {
            if ((comp[__item6] == (-1)))
            {
                dfsBackward(__item6, compCount);
                compCount += 1;
            }
        }
        int[] indeg = new int[compCount];
        HashSet<(int, int)> seenEdges = new();
        for (int __item7 = 0; __item7 < n; __item7++)
        {
            {
                foreach (var __item8 in graph[__item7])
                {
                    var cu = comp[__item7];
                    var cv = comp[__item8];
                    if (((cu != cv) && seenEdges.Add((cu, cv))))
                    {
                        indeg[cv] += 1;
                    }
                }
            }
        }
        var sources = System.Linq.Enumerable.Select(__PscpSeq.rangeInt(0, compCount, false), c => c).filter(c => (indeg[c] == 0)).map(c => (c + 1));
        stdout.writeln(compCount);
        stdout.join(' ', sources);
        stdout.writeln("");
    }

    public static void Main()
    {
        Run();
        stdout.flush();
    }

    private static void Run()
    {
        solve();
    }
}

public static class __PscpSeq{public static IEnumerable<int> rangeInt(int start,int end,bool inclusive)=>rangeInt(start,end,1,inclusive);public static IEnumerable<int> rangeInt(int start,int end,int step,bool inclusive){
#if DEBUG
if(step==0) throw new InvalidOperationException("Range step cannot be zero.");
#else
if(step==0) yield break;
#endif
if(step> 0){for(int i=start;inclusive? i<=end:i<end;i+=step){yield return i;}yield break;}for(int i=start;inclusive? i>=end:i> end;i+=step){yield return i;}}public static TResult[] map<T,TResult>(this IEnumerable<T> source,Func<T,TResult> selector){List<TResult> result=new();foreach(T item in source){result.Add(selector(item));}return result.ToArray();}public static T[] filter<T>(this IEnumerable<T> source,Func<T,bool> predicate){List<T> result=new();foreach(T item in source){if(predicate(item)){result.Add(item);}}return result.ToArray();}public static T[] reverse<T>(this IEnumerable<T> source)=>source.Reverse().ToArray();}

public sealed class __PscpStdin{private readonly StreamReader _reader=new(Console.OpenStandardInput(),Encoding.UTF8,false,1<<16);public int readInt()=>ReadInt();private int SkipTokenSeparators(){int next;while((next=_reader.Peek())>=0&&char.IsWhiteSpace((char)next)){_reader.Read();}return next;}private int ReadInt(){int next=SkipTokenSeparators();
#if DEBUG
if(next<0){throw new EndOfStreamException("Unexpected end of input.");}
#endif
bool negative=false;if(next is'+'or'-'){negative=next=='-';_reader.Read();next=_reader.Peek();}
#if DEBUG
if(next<0||!char.IsDigit((char)next)){throw new FormatException("Expected an integer token.");}
#endif
long value=0;
#if DEBUG
bool hasDigits=false;
#endif
while((next=_reader.Peek())>=0&&char.IsDigit((char)next)){
#if DEBUG
hasDigits=true;
#endif
value=(value*10L)+(_reader.Read()-'0');}
#if DEBUG
if(!hasDigits){throw new FormatException("Expected an integer token.");}
#endif
#if DEBUG
if(next>=0&&!char.IsWhiteSpace((char)next)){throw new FormatException("Invalid integer token.");}if((!negative&&value> int.MaxValue)||(negative&&value> 2147483648L)){throw new OverflowException("Integer token is out of range for int.");}
#endif
return negative? unchecked((int)(-value)):unchecked((int)value);}}

public sealed class __PscpStdout{private readonly StreamWriter _writer=new(Console.OpenStandardOutput(),new UTF8Encoding(false),1<<16){AutoFlush=false};public void flush()=>_writer.Flush();public void write(int value)=>_writer.Write(value.ToString(CultureInfo.InvariantCulture));public void write(long value)=>_writer.Write(value.ToString(CultureInfo.InvariantCulture));public void write(double value)=>_writer.Write(value.ToString(CultureInfo.InvariantCulture));public void write(decimal value)=>_writer.Write(value.ToString(CultureInfo.InvariantCulture));public void write(bool value)=>_writer.Write(value?"True":"False");public void write(char value)=>_writer.Write(value);public void write(string? value)=>_writer.Write(value??string.Empty);public void write<T1,T2>((T1,T2) value){WriteValue(value.Item1);_writer.Write(' ');WriteValue(value.Item2);}public void write<T1,T2,T3>((T1,T2,T3) value){WriteValue(value.Item1);_writer.Write(' ');WriteValue(value.Item2);_writer.Write(' ');WriteValue(value.Item3);}public void write(int[] values)=>WriteJoined(values);public void write(long[] values)=>WriteJoined(values);public void write(double[] values)=>WriteJoined(values);public void write(decimal[] values)=>WriteJoined(values);public void write(bool[] values)=>WriteJoined(values);public void write(char[] values)=>WriteJoined(values);public void write(string[] values)=>WriteJoined(values);public void write<T>(T[] values)=>WriteJoined(values);public void write<T>(IEnumerable<T> values)=>WriteJoined(values);public void write<T>(T value)=>WriteValue(value);public void writeln()=>_writer.WriteLine();public void writeln(int value){write(value);_writer.WriteLine();}public void writeln(long value){write(value);_writer.WriteLine();}public void writeln(double value){write(value);_writer.WriteLine();}public void writeln(decimal value){write(value);_writer.WriteLine();}public void writeln(bool value){write(value);_writer.WriteLine();}public void writeln(char value){write(value);_writer.WriteLine();}public void writeln(string? value){write(value);_writer.WriteLine();}public void writeln<T1,T2>((T1,T2) value){write(value);_writer.WriteLine();}public void writeln<T1,T2,T3>((T1,T2,T3) value){write(value);_writer.WriteLine();}public void writeln(int[] values){write(values);_writer.WriteLine();}public void writeln(long[] values){write(values);_writer.WriteLine();}public void writeln(double[] values){write(values);_writer.WriteLine();}public void writeln(decimal[] values){write(values);_writer.WriteLine();}public void writeln(bool[] values){write(values);_writer.WriteLine();}public void writeln(char[] values){write(values);_writer.WriteLine();}public void writeln(string[] values){write(values);_writer.WriteLine();}public void writeln<T>(T[] values){write(values);_writer.WriteLine();}public void writeln<T>(IEnumerable<T> values){write(values);_writer.WriteLine();}public void writeln<T>(T value){write(value);_writer.WriteLine();}public void lines<T>(IEnumerable<T> values){foreach(T value in values){writeln(value);}}public void grid<T>(IEnumerable<IEnumerable<T>> grid){foreach(IEnumerable<T> row in grid){writeln(row);}}public void join<T>(string separator,IEnumerable<T> values){bool first=true;foreach(T value in values){if(!first) _writer.Write(separator);first=false;WriteValue(value);}}private void WriteJoined<T>(IEnumerable<T> values){bool first=true;foreach(T value in values){if(!first) _writer.Write(' ');first=false;WriteValue(value);}}private void WriteValue(int value)=>_writer.Write(value.ToString(CultureInfo.InvariantCulture));private void WriteValue(long value)=>_writer.Write(value.ToString(CultureInfo.InvariantCulture));private void WriteValue(double value)=>_writer.Write(value.ToString(CultureInfo.InvariantCulture));private void WriteValue(decimal value)=>_writer.Write(value.ToString(CultureInfo.InvariantCulture));private void WriteValue(bool value)=>_writer.Write(value?"True":"False");private void WriteValue(char value)=>_writer.Write(value);private void WriteValue(string? value)=>_writer.Write(value??string.Empty);private void WriteValue<T1,T2>((T1,T2) value){WriteValue(value.Item1);_writer.Write(' ');WriteValue(value.Item2);}private void WriteValue<T1,T2,T3>((T1,T2,T3) value){WriteValue(value.Item1);_writer.Write(' ');WriteValue(value.Item2);_writer.Write(' ');WriteValue(value.Item3);}private void WriteValue<T>(T value)=>_writer.Write(__PscpRender.format(value));}

public static class __PscpRender{public static string format<T>(T value){return value switch{null=>string.Empty,string text=>text,bool boolean=>boolean?"True":"False",char ch=>ch.ToString(),System.Runtime.CompilerServices.ITuple tuple=>FormatTuple(tuple),System.Collections.IEnumerable enumerable when value is not string=>FormatEnumerable(enumerable),IFormattable formattable=>formattable.ToString(null,CultureInfo.InvariantCulture)??string.Empty,_=>value?.ToString()??string.Empty,};}private static string FormatTuple(System.Runtime.CompilerServices.ITuple tuple){StringBuilder builder=new();for(int i=0;i<tuple.Length;i++){if(i> 0) builder.Append(' ');builder.Append(format(tuple[i]));}return builder.ToString();}private static string FormatEnumerable(System.Collections.IEnumerable enumerable){StringBuilder builder=new();bool first=true;foreach(object? item in enumerable){if(!first) builder.Append(' ');first=false;builder.Append(format(item));}return builder.ToString();}}
