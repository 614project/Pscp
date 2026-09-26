namespace Pscp.Transpiler;

// Templates of the generated runtime classes. They must stay within C# 10 and .NET 6 APIs. Members are pruned
// by name (see CSharpEmitter.Runtime.cs), so every member starts on its own line at class level.
internal sealed partial class CSharpEmitter
{
    // Input (spec §17). Tokens are separated by spaces, tabs and line breaks. The cursor is either at the start
    // of a line (initially and after a line read) or inside a line (after a token or character read), which
    // decides what `readLine` returns (spec §17.4). Returned lines never contain `\r` or `\n`.
    private const string StdinTemplate =
        """
        public sealed class __PscpStdin
        {
            private readonly Stream _stream = Console.OpenStandardInput();
            private readonly byte[] _buffer = new byte[1 << 16];
            private byte[] _text = new byte[256];
            private int _length;
            private int _position;
            internal bool _lineStart = true;
            private static readonly char[] WordSeparators = { ' ', '\t', '\f', '\v' };

            public int readInt()
            {
                long value = ReadInteger();
            #if DEBUG
                if (value < int.MinValue || value > int.MaxValue) throw new OverflowException("The input value is out of range for int.");
            #endif
                return unchecked((int)value);
            }

            public long readLong() => ReadInteger();

            public double readDouble() => double.Parse(ReadToken(), NumberStyles.Float, CultureInfo.InvariantCulture);

            public decimal readDecimal() => decimal.Parse(ReadToken(), NumberStyles.Number, CultureInfo.InvariantCulture);

            public bool readBool() => ParseBool(ReadToken());

            public string readString() => ReadToken();

            public char readChar()
            {
                int next = SkipSpaces();
            #if DEBUG
                if (next < 0) throw new EndOfStreamException("Input ended while reading a character.");
            #endif
                _lineStart = false;
                if (next < 0x80)
                {
                    Read();
                    return next < 0 ? '\0' : (char)next;
                }

                return ReadUtf8Char();
            }

            public bool hasNext()
            {
                int next;
                bool skipped = false;
                while ((next = Peek()) >= 0 && IsSpace(next))
                {
                    Read();
                    skipped = true;
                }

                if (skipped) _lineStart = false;
                return next >= 0;
            }

            public bool hasNextLine()
            {
                if (!_lineStart)
                {
                    int next = SkipBlanks();
                    if (next is not ('\r' or '\n')) return next >= 0;
                    SkipLineBreak();
                }

                return Peek() >= 0;
            }

            public string readLine()
            {
                if (!_lineStart)
                {
                    int next = SkipBlanks();
                    if (next is not ('\r' or '\n') && next >= 0) return ReadRestOfLineCore();
                    if (next >= 0) SkipLineBreak();
                }

            #if DEBUG
                if (Peek() < 0) throw new EndOfStreamException("Input ended while reading a line.");
            #endif
                return ReadRestOfLineCore();
            }

            public string readRestOfLine() => ReadRestOfLineCore();

            public string[] readLines(int n)
            {
                string[] result = new string[n];
                for (int i = 0; i < n; i++) result[i] = readLine();
                return result;
            }

            public string[] readWords() => readLine().Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

            public char[] readChars() => readLine().ToCharArray();

            public char[][] readCharGrid(int n)
            {
                char[][] result = new char[n][];
                for (int i = 0; i < n; i++) result[i] = readChars();
                return result;
            }

            public string[][] readWordGrid(int n)
            {
                string[][] result = new string[n][];
                for (int i = 0; i < n; i++) result[i] = readWords();
                return result;
            }

            public int[] readArrayInt(int n)
            {
                int[] result = new int[n];
                for (int i = 0; i < n; i++) result[i] = readInt();
                return result;
            }

            public long[] readArrayLong(int n)
            {
                long[] result = new long[n];
                for (int i = 0; i < n; i++) result[i] = readLong();
                return result;
            }

            public double[] readArrayDouble(int n)
            {
                double[] result = new double[n];
                for (int i = 0; i < n; i++) result[i] = readDouble();
                return result;
            }

            public decimal[] readArrayDecimal(int n)
            {
                decimal[] result = new decimal[n];
                for (int i = 0; i < n; i++) result[i] = readDecimal();
                return result;
            }

            public bool[] readArrayBool(int n)
            {
                bool[] result = new bool[n];
                for (int i = 0; i < n; i++) result[i] = readBool();
                return result;
            }

            public char[] readArrayChar(int n)
            {
                char[] result = new char[n];
                for (int i = 0; i < n; i++) result[i] = readChar();
                return result;
            }

            public string[] readArrayString(int n)
            {
                string[] result = new string[n];
                for (int i = 0; i < n; i++) result[i] = readString();
                return result;
            }

            public int[][] readGridInt(int n, int m)
            {
                int[][] result = new int[n][];
                for (int i = 0; i < n; i++) result[i] = readArrayInt(m);
                return result;
            }

            public long[][] readGridLong(int n, int m)
            {
                long[][] result = new long[n][];
                for (int i = 0; i < n; i++) result[i] = readArrayLong(m);
                return result;
            }

            public char[][] readGridChar(int n, int m)
            {
                char[][] result = new char[n][];
                for (int i = 0; i < n; i++) result[i] = readArrayChar(m);
                return result;
            }

            private int Peek()
            {
                if (_position == _length)
                {
                    _length = _stream.Read(_buffer, 0, _buffer.Length);
                    _position = 0;
                    if (_length <= 0)
                    {
                        _length = 0;
                        return -1;
                    }
                }

                return _buffer[_position];
            }

            private int Read()
            {
                int next = Peek();
                if (next >= 0) _position++;
                return next;
            }

            private static bool IsSpace(int ch) => ch is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

            private int SkipSpaces()
            {
                int next;
                while ((next = Peek()) >= 0 && IsSpace(next)) Read();
                return next;
            }

            private int SkipBlanks()
            {
                int next;
                while ((next = Peek()) is ' ' or '\t' or '\f' or '\v') Read();
                return next;
            }

            private void SkipLineBreak()
            {
                if (Peek() == '\r') Read();
                if (Peek() == '\n') Read();
                _lineStart = true;
            }

            private void Append(int length, int value)
            {
                if (length == _text.Length) Array.Resize(ref _text, length * 2);
                _text[length] = (byte)value;
            }

            private string ReadToken()
            {
                int next = SkipSpaces();
            #if DEBUG
                if (next < 0) throw new EndOfStreamException("Input ended while reading a token.");
            #endif
                int length = 0;
                while ((next = Peek()) >= 0 && !IsSpace(next))
                {
                    Append(length++, Read());
                }

                _lineStart = false;
                return Encoding.UTF8.GetString(_text, 0, length);
            }

            private string ReadRestOfLineCore()
            {
                int length = 0;
                int next;
                while ((next = Peek()) >= 0 && next != '\n')
                {
                    Append(length++, Read());
                }

                if (next == '\n') Read();
                if (length > 0 && _text[length - 1] == '\r') length--;
                _lineStart = true;
                return Encoding.UTF8.GetString(_text, 0, length);
            }

            private char ReadUtf8Char()
            {
                int lead = Read();
                int count = lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC0 ? 2 : 1;
                Span<byte> bytes = stackalloc byte[4];
                bytes[0] = (byte)lead;
                for (int i = 1; i < count; i++)
                {
                    int next = Peek();
                    if (next < 0x80 || next >= 0xC0) { count = i; break; }
                    bytes[i] = (byte)Read();
                }

                Span<char> chars = stackalloc char[2];
                int decoded = Encoding.UTF8.GetChars(bytes.Slice(0, count), chars);
                return decoded > 0 ? chars[0] : '�';
            }

            private long ReadInteger()
            {
                int next = SkipSpaces();
            #if DEBUG
                if (next < 0) throw new EndOfStreamException("Input ended while reading an integer.");
            #endif
                bool negative = false;
                if (next is '+' or '-')
                {
                    negative = next == '-';
                    Read();
                }

                ulong value = 0;
            #if DEBUG
                bool hasDigits = false;
                bool overflow = false;
            #endif
                while ((next = Peek()) >= '0' && next <= '9')
                {
                    Read();
            #if DEBUG
                    hasDigits = true;
                    overflow |= value > 1844674407370955161UL;
            #endif
                    value = unchecked(value * 10 + (ulong)(next - '0'));
                }

                _lineStart = false;
            #if DEBUG
                if (!hasDigits || (next >= 0 && !IsSpace(next))) throw new FormatException("The input token is not an integer.");
                if (overflow || value > (negative ? 9223372036854775808UL : 9223372036854775807UL)) throw new OverflowException("The input value is out of range for long.");
            #endif
                return negative ? unchecked(-(long)value) : unchecked((long)value);
            }

            private static bool ParseBool(string token)
            {
                if (token == "1" || string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (token == "0" || string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;
            #if DEBUG
                throw new FormatException($"`{token}` is not a bool: use true, false, 1 or 0.");
            #else
                return false;
            #endif
            }
        }
        """;

    // Output (spec §18). Everything is written to one buffer that `Console.Out` shares; lines end with `\n`.
    private const string StdoutTemplate =
        """
        public sealed class __PscpStdout
        {
            private readonly StreamWriter _writer = new(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16) { AutoFlush = false, NewLine = "\n" };

            public __PscpStdout() => Console.SetOut(_writer);

            public void flush() => _writer.Flush();

            public void write(int value) => __PscpRender.writeInt(_writer, value);
            public void write(long value) => __PscpRender.writeLong(_writer, value);
            public void write(double value) => _writer.Write(__PscpRender.formatDouble(value));
            public void write(float value) => _writer.Write(__PscpRender.formatFloat(value));
            public void write(decimal value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
            public void write(bool value) => _writer.Write(value ? "true" : "false");
            public void write(char value) => _writer.Write(value);
            public void write(string? value) => _writer.Write(value);
            public void write(char[] values) => _writer.Write(values);
            public void write(int[] values) => __PscpRender.writeInts(_writer, values);
            public void write(long[] values) => __PscpRender.writeLongs(_writer, values);
            public void write<T>(T value) => __PscpRender.write(_writer, value);

            public void writeln() => _writer.Write('\n');
            public void writeln(int value) { write(value); _writer.Write('\n'); }
            public void writeln(long value) { write(value); _writer.Write('\n'); }
            public void writeln(double value) { write(value); _writer.Write('\n'); }
            public void writeln(float value) { write(value); _writer.Write('\n'); }
            public void writeln(decimal value) { write(value); _writer.Write('\n'); }
            public void writeln(bool value) { write(value); _writer.Write('\n'); }
            public void writeln(char value) { write(value); _writer.Write('\n'); }
            public void writeln(string? value) { write(value); _writer.Write('\n'); }
            public void writeln(char[] values) { write(values); _writer.Write('\n'); }
            public void writeln(int[] values) { write(values); _writer.Write('\n'); }
            public void writeln(long[] values) { write(values); _writer.Write('\n'); }
            public void writeln<T>(T value) { write(value); _writer.Write('\n'); }

            public void lines<T>(IEnumerable<T> values)
            {
                foreach (T value in values) writeln(value);
            }

            public void grid<T>(IEnumerable<T> rows)
            {
                foreach (T row in rows) writeln(row);
            }

            public void join<T>(string separator, IEnumerable<T> values)
            {
                bool first = true;
                foreach (T value in values)
                {
                    if (!first) _writer.Write(separator);
                    first = false;
                    write(value);
                }
            }

            public void join<T>(char separator, IEnumerable<T> values)
            {
                bool first = true;
                foreach (T value in values)
                {
                    if (!first) _writer.Write(separator);
                    first = false;
                    write(value);
                }
            }
        }
        """;

    // Rendering rules (spec §18.3), shared by output, `string x` and interpolation holes without a format.
    private const string RenderTemplate =
        """
        public static class __PscpRender
        {
            public static string format<T>(T value)
            {
                StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
                write(writer, value);
                return writer.ToString();
            }

            public static string formatBool(bool value) => value ? "true" : "false";

            // `bool s` parses like `stdin.readBool()`: true, false, 1 or 0, in any case (spec §21.1).
            public static bool parseBool(string text)
            {
                if (text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (text == "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
                throw new FormatException($"`{text}` is not a bool: use true, false, 1 or 0.");
            }

            // `char s` needs a string of length 1 (spec §21.2).
            public static char parseChar(string text)
            {
            #if DEBUG
                if (text.Length != 1) throw new FormatException($"`{text}` is not a single character.");
            #endif
                return text[0];
            }

            public static void write<T>(TextWriter writer, T value) => WriteObject(writer, value);

            public static void writeInt(TextWriter writer, int value)
            {
                Span<char> buffer = stackalloc char[11];
                value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
                writer.Write(buffer.Slice(0, written));
            }

            public static void writeLong(TextWriter writer, long value)
            {
                Span<char> buffer = stackalloc char[20];
                value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
                writer.Write(buffer.Slice(0, written));
            }

            public static void writeInts(TextWriter writer, int[] values)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) writer.Write(' ');
                    writeInt(writer, values[i]);
                }
            }

            public static void writeLongs(TextWriter writer, long[] values)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) writer.Write(' ');
                    writeLong(writer, values[i]);
                }
            }

            public static string formatDouble(double value)
            {
                if (double.IsNaN(value)) return "NaN";
                if (double.IsPositiveInfinity(value)) return "Infinity";
                if (double.IsNegativeInfinity(value)) return "-Infinity";
                if (value == 0) return "0";
                return ExpandExponent(value.ToString("R", CultureInfo.InvariantCulture));
            }

            public static string formatFloat(float value)
            {
                if (float.IsNaN(value)) return "NaN";
                if (float.IsPositiveInfinity(value)) return "Infinity";
                if (float.IsNegativeInfinity(value)) return "-Infinity";
                if (value == 0) return "0";
                return ExpandExponent(value.ToString("R", CultureInfo.InvariantCulture));
            }

            private static string ExpandExponent(string text)
            {
                int exponentIndex = text.IndexOfAny(new[] { 'E', 'e' });
                if (exponentIndex < 0) return text;
                bool negative = text[0] == '-';
                string mantissa = text.Substring(negative ? 1 : 0, exponentIndex - (negative ? 1 : 0));
                int exponent = int.Parse(text.Substring(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                int point = mantissa.IndexOf('.');
                string digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
                int integerDigits = (point < 0 ? mantissa.Length : point) + exponent;
                string result = integerDigits <= 0
                    ? "0." + new string('0', -integerDigits) + digits
                    : integerDigits >= digits.Length
                        ? digits + new string('0', integerDigits - digits.Length)
                        : digits.Substring(0, integerDigits) + "." + digits.Substring(integerDigits);
                return negative ? "-" + result : result;
            }

            private static void WriteObject(TextWriter writer, object? value)
            {
                switch (value)
                {
                    case null:
                        return;
                    case string text:
                        writer.Write(text);
                        return;
                    case bool boolean:
                        writer.Write(boolean ? "true" : "false");
                        return;
                    case char ch:
                        writer.Write(ch);
                        return;
                    case int number:
                        writeInt(writer, number);
                        return;
                    case long number:
                        writeLong(writer, number);
                        return;
                    case double number:
                        writer.Write(formatDouble(number));
                        return;
                    case float number:
                        writer.Write(formatFloat(number));
                        return;
                    case IFormattable formattable:
                        writer.Write(formattable.ToString(null, CultureInfo.InvariantCulture));
                        return;
                    case System.Runtime.CompilerServices.ITuple tuple:
                        for (int i = 0; i < tuple.Length; i++)
                        {
                            if (i > 0) writer.Write(' ');
                            WriteObject(writer, tuple[i]);
                        }

                        return;
                    case System.Collections.IEnumerable sequence:
                        WriteSequence(writer, sequence);
                        return;
                }

                Type type = value.GetType();
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    WriteObject(writer, type.GetProperty("Key")!.GetValue(value));
                    writer.Write(' ');
                    WriteObject(writer, type.GetProperty("Value")!.GetValue(value));
                    return;
                }

                writer.Write(value.ToString());
            }

            // Characters are joined without a separator, scalars with a space, tuples and nested collections one
            // per line.
            private static void WriteSequence(TextWriter writer, System.Collections.IEnumerable sequence)
            {
                switch (sequence)
                {
                    case char[] chars:
                        writer.Write(chars);
                        return;
                    case int[] ints:
                        writeInts(writer, ints);
                        return;
                    case long[] longs:
                        writeLongs(writer, longs);
                        return;
                }

                int kind = ElementKind(sequence.GetType());
                bool first = true;
                foreach (object? item in sequence)
                {
                    if (!first && kind != 1) writer.Write(kind == 2 ? '\n' : ' ');
                    first = false;
                    WriteObject(writer, item);
                }
            }

            private static int ElementKind(Type sequenceType)
            {
                Type? element = sequenceType.IsArray ? sequenceType.GetElementType() : FindElementType(sequenceType);
                if (element is null || element == typeof(string)) return 0;
                if (element == typeof(char)) return 1;
                if (typeof(System.Runtime.CompilerServices.ITuple).IsAssignableFrom(element) || typeof(System.Collections.IEnumerable).IsAssignableFrom(element)) return 2;
                return element.IsGenericType && element.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ? 2 : 0;
            }

            private static Type? FindElementType(Type type)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GetGenericArguments()[0];
                foreach (Type candidate in type.GetInterfaces())
                {
                    if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return candidate.GetGenericArguments()[0];
                }

                return null;
            }
        }
        """;

    // Aggregates, math and collection helpers (spec §22-§24). Collection helpers never change their receiver and
    // return arrays; sorts are stable and use the PSCP default order.
    private const string SequenceHelpersTemplate =
        """
        public static class __PscpSeq
        {
            public static T[] arrayOf<T>(params T[] items) => items;
            public static IEnumerable<T> one<T>(T value) => new[] { value };
            public static IEnumerable<T> concat<T>(params IEnumerable<T>[] sequences) => sequences.SelectMany(static sequence => sequence);
            public static T[] toArray<T>(IEnumerable<T> sequence) => sequence as T[] ?? sequence.ToArray();
            public static List<T> toList<T>(IEnumerable<T> sequence) => sequence.ToList();
            public static LinkedList<T> toLinkedList<T>(IEnumerable<T> sequence) => new(sequence);
            public static int compare<T>(T left, T right) => __PscpOrder<T>.Asc.Compare(left, right);
            public static List<T> sliceList<T>(List<T> list, int start, int end) => list.GetRange(start, end - start);

            public static int add(int left, int right)
            {
            #if DEBUG
                return checked(left + right);
            #else
                return unchecked(left + right);
            #endif
            }

            public static long add(long left, long right)
            {
            #if DEBUG
                return checked(left + right);
            #else
                return unchecked(left + right);
            #endif
            }

            public static double add(double left, double right) => left + right;
            public static decimal add(decimal left, decimal right) => left + right;

            public static int gcd(int left, int right)
            {
                uint a = left >= 0 ? (uint)left : (uint)(-(long)left);
                uint b = right >= 0 ? (uint)right : (uint)(-(long)right);
                while (b != 0)
                {
                    uint next = a % b;
                    a = b;
                    b = next;
                }

                return unchecked((int)a);
            }

            public static long gcd(long left, long right)
            {
                ulong a = left >= 0 ? (ulong)left : (ulong)(-(left + 1)) + 1UL;
                ulong b = right >= 0 ? (ulong)right : (ulong)(-(right + 1)) + 1UL;
                while (b != 0)
                {
                    ulong next = a % b;
                    a = b;
                    b = next;
                }

                return unchecked((long)a);
            }

            public static int lcm(int left, int right) => left == 0 || right == 0 ? 0 : (int)Math.Abs((long)left / gcd(left, right) * right);
            public static long lcm(long left, long right) => left == 0 || right == 0 ? 0L : Math.Abs((left / gcd(left, right)) * right);
            public static int popcount(int value) => System.Numerics.BitOperations.PopCount(unchecked((uint)value));
            public static int popcount(long value) => System.Numerics.BitOperations.PopCount(unchecked((ulong)value));

            public static int bitLength(int value)
            {
            #if DEBUG
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "bitLength needs a non-negative value.");
            #endif
                return value <= 0 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)value);
            }

            public static int bitLength(long value)
            {
            #if DEBUG
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "bitLength needs a non-negative value.");
            #endif
                return value <= 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)value);
            }

            public static int pow(int value, int exponent)
            {
            #if DEBUG
                if (exponent < 0) throw new ArgumentOutOfRangeException(nameof(exponent), "pow needs a non-negative exponent.");
                checked
                {
            #endif
                int result = 1;
                while (exponent > 0)
                {
                    if ((exponent & 1) != 0) result *= value;
                    exponent >>= 1;
                    if (exponent > 0) value *= value;
                }

                return result;
            #if DEBUG
                }
            #endif
            }

            public static long pow(long value, long exponent)
            {
            #if DEBUG
                if (exponent < 0) throw new ArgumentOutOfRangeException(nameof(exponent), "pow needs a non-negative exponent.");
                checked
                {
            #endif
                long result = 1;
                while (exponent > 0)
                {
                    if ((exponent & 1) != 0) result *= value;
                    exponent >>= 1;
                    if (exponent > 0) value *= value;
                }

                return result;
            #if DEBUG
                }
            #endif
            }

            public static double pow(double value, double exponent) => Math.Pow(value, exponent);

            public static long powMod(long value, long exponent, long modulus)
            {
            #if DEBUG
                if (exponent < 0) throw new ArgumentOutOfRangeException(nameof(exponent), "pow needs a non-negative exponent.");
                if (modulus < 1) throw new ArgumentOutOfRangeException(nameof(modulus), "pow(a, e, m) needs m >= 1.");
            #endif
                ulong m = (ulong)modulus;
                long reduced = value % modulus;
                if (reduced < 0) reduced += modulus;
                ulong baseValue = (ulong)reduced;
                ulong result = 1 % m;
                while (exponent > 0)
                {
                    if ((exponent & 1) != 0) result = MulMod(result, baseValue, m);
                    exponent >>= 1;
                    if (exponent > 0) baseValue = MulMod(baseValue, baseValue, m);
                }

                return (long)result;
            }

            private static ulong MulMod(ulong left, ulong right, ulong modulus)
            {
                ulong high = Math.BigMul(left, right, out ulong low);
                if (high == 0) return low % modulus;
                return (ulong)((((System.Numerics.BigInteger)high << 64) | low) % modulus);
            }

            public static int floorDiv(int left, int right)
            {
                int quotient = left / right;
                return left % right != 0 && (left < 0) != (right < 0) ? quotient - 1 : quotient;
            }

            public static long floorDiv(long left, long right)
            {
                long quotient = left / right;
                return left % right != 0 && (left < 0) != (right < 0) ? quotient - 1 : quotient;
            }

            public static int ceilDiv(int left, int right)
            {
                int quotient = left / right;
                return left % right != 0 && (left < 0) == (right < 0) ? quotient + 1 : quotient;
            }

            public static long ceilDiv(long left, long right)
            {
                long quotient = left / right;
                return left % right != 0 && (left < 0) == (right < 0) ? quotient + 1 : quotient;
            }

            public static IEnumerable<int> rangeInt(int start, int end, bool inclusive) => rangeInt(start, end, 1, inclusive);

            public static IEnumerable<int> rangeInt(int start, int end, int step, bool inclusive)
            {
            #if DEBUG
                checkRange(start, end, step, inclusive, int.MinValue, int.MaxValue);
            #endif
                if (step > 0)
                {
                    for (long i = start; inclusive ? i <= end : i < end; i += step) yield return (int)i;
                }
                else if (step < 0)
                {
                    for (long i = start; inclusive ? i >= end : i > end; i += step) yield return (int)i;
                }
            }

            public static IEnumerable<long> rangeLong(long start, long end, bool inclusive) => rangeLong(start, end, 1L, inclusive);

            public static IEnumerable<long> rangeLong(long start, long end, long step, bool inclusive)
            {
            #if DEBUG
                checkRange(start, end, step, inclusive, long.MinValue, long.MaxValue);
            #endif
                if (step > 0)
                {
                    for (long i = start; inclusive ? i <= end : i < end; i += step)
                    {
                        yield return i;
                        if (i > long.MaxValue - step) yield break;
                    }
                }
                else if (step < 0)
                {
                    for (long i = start; inclusive ? i >= end : i > end; i += step)
                    {
                        yield return i;
                        if (i < long.MinValue - step) yield break;
                    }
                }
            }

            public static IEnumerable<char> rangeChar(char start, char end, bool inclusive) => rangeChar(start, end, 1, inclusive);

            public static IEnumerable<char> rangeChar(char start, char end, int step, bool inclusive)
            {
            #if DEBUG
                checkRange(start, end, step, inclusive, char.MinValue, char.MaxValue);
            #endif
                if (step > 0)
                {
                    for (int i = start; inclusive ? i <= end : i < end; i += step) yield return (char)i;
                }
                else if (step < 0)
                {
                    for (int i = start; inclusive ? i >= end : i > end; i += step) yield return (char)i;
                }
            }

            // Spec §14.4: the value after the last element of a non-empty range must fit in the element type.
            public static void checkRange(long start, long end, long step, bool inclusive, long min, long max)
            {
                if (step > 0 && (inclusive ? start <= end : start < end))
                {
                    ulong span = unchecked((ulong)(end - start)) - (inclusive ? 0UL : 1UL);
                    long last = unchecked(start + (long)(span / (ulong)step * (ulong)step));
                    if (last > max - step) throw new OverflowException("The next value of the range overflows its element type (spec §14.4).");
                }
                else if (step < 0 && (inclusive ? start >= end : start > end))
                {
                    ulong span = unchecked((ulong)(start - end)) - (inclusive ? 0UL : 1UL);
                    ulong magnitude = unchecked((ulong)(-step));
                    long last = unchecked(start - (long)(span / magnitude * magnitude));
                    if (last < min - step) throw new OverflowException("The next value of the range overflows its element type (spec §14.4).");
                }
            }

            public static TResult[] map<T, TResult>(this IEnumerable<T> source, Func<T, TResult> selector)
            {
                List<TResult> result = new();
                foreach (T item in source) result.Add(selector(item));
                return result.ToArray();
            }

            public static T[] filter<T>(this IEnumerable<T> source, Func<T, bool> predicate)
            {
                List<T> result = new();
                foreach (T item in source)
                {
                    if (predicate(item)) result.Add(item);
                }

                return result.ToArray();
            }

            public static TState fold<T, TState>(this IEnumerable<T> source, TState seed, Func<TState, T, TState> folder)
            {
                TState state = seed;
                foreach (T item in source) state = folder(state, item);
                return state;
            }

            public static TState[] scan<T, TState>(this IEnumerable<T> source, TState seed, Func<TState, T, TState> folder)
            {
                List<TState> values = new() { seed };
                TState state = seed;
                foreach (T item in source)
                {
                    state = folder(state, item);
                    values.Add(state);
                }

                return values.ToArray();
            }

            public static (TResult[] mapped, TState state) mapFold<T, TResult, TState>(this IEnumerable<T> source, TState seed, Func<TState, T, (TResult mapped, TState state)> folder)
            {
                List<TResult> values = new();
                TState state = seed;
                foreach (T item in source)
                {
                    (TResult mapped, TState next) = folder(state, item);
                    values.Add(mapped);
                    state = next;
                }

                return (values.ToArray(), state);
            }

            public static int sum(this IEnumerable<int> source)
            {
                int total = 0;
                foreach (int item in source) total = add(total, item);
                return total;
            }

            public static long sum(this IEnumerable<long> source)
            {
                long total = 0;
                foreach (long item in source) total = add(total, item);
                return total;
            }

            public static double sum(this IEnumerable<double> source)
            {
                double total = 0d;
                foreach (double item in source) total += item;
                return total;
            }

            public static decimal sum(this IEnumerable<decimal> source)
            {
                decimal total = 0m;
                foreach (decimal item in source) total += item;
                return total;
            }

            public static long sumLong(this IEnumerable<int> source)
            {
                long total = 0;
                foreach (int item in source) total = add(total, item);
                return total;
            }

            public static int sumBy<T>(this IEnumerable<T> source, Func<T, int> selector)
            {
                int total = 0;
                foreach (T item in source) total = add(total, selector(item));
                return total;
            }

            public static long sumBy<T>(this IEnumerable<T> source, Func<T, long> selector)
            {
                long total = 0;
                foreach (T item in source) total = add(total, selector(item));
                return total;
            }

            public static double sumBy<T>(this IEnumerable<T> source, Func<T, double> selector)
            {
                double total = 0d;
                foreach (T item in source) total += selector(item);
                return total;
            }

            public static decimal sumBy<T>(this IEnumerable<T> source, Func<T, decimal> selector)
            {
                decimal total = 0m;
                foreach (T item in source) total += selector(item);
                return total;
            }

            public static T min<T>(T left, T right) => __PscpOrder<T>.Asc.Compare(right, left) < 0 ? right : left;
            public static T max<T>(T left, T right) => __PscpOrder<T>.Asc.Compare(right, left) > 0 ? right : left;

            public static T min<T>(this IEnumerable<T> source)
            {
                IComparer<T> comparer = __PscpOrder<T>.Asc;
                using IEnumerator<T> enumerator = source.GetEnumerator();
                if (!enumerator.MoveNext())
                {
            #if DEBUG
                    throw new InvalidOperationException("min of an empty sequence.");
            #else
                    return default!;
            #endif
                }

                T best = enumerator.Current;
                while (enumerator.MoveNext())
                {
                    if (comparer.Compare(enumerator.Current, best) < 0) best = enumerator.Current;
                }

                return best;
            }

            public static T max<T>(this IEnumerable<T> source)
            {
                IComparer<T> comparer = __PscpOrder<T>.Asc;
                using IEnumerator<T> enumerator = source.GetEnumerator();
                if (!enumerator.MoveNext())
                {
            #if DEBUG
                    throw new InvalidOperationException("max of an empty sequence.");
            #else
                    return default!;
            #endif
                }

                T best = enumerator.Current;
                while (enumerator.MoveNext())
                {
                    if (comparer.Compare(enumerator.Current, best) > 0) best = enumerator.Current;
                }

                return best;
            }

            public static T minBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector) => bestBy(source, selector, -1);
            public static T maxBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector) => bestBy(source, selector, 1);

            private static T bestBy<T, TKey>(IEnumerable<T> source, Func<T, TKey> selector, int direction)
            {
                IComparer<TKey> comparer = __PscpOrder<TKey>.Asc;
                using IEnumerator<T> enumerator = source.GetEnumerator();
                if (!enumerator.MoveNext())
                {
            #if DEBUG
                    throw new InvalidOperationException(direction < 0 ? "minBy of an empty sequence." : "maxBy of an empty sequence.");
            #else
                    return default!;
            #endif
                }

                T bestItem = enumerator.Current;
                TKey bestKey = selector(bestItem);
                while (enumerator.MoveNext())
                {
                    T item = enumerator.Current;
                    TKey key = selector(item);
                    if (comparer.Compare(key, bestKey) * direction > 0)
                    {
                        bestItem = item;
                        bestKey = key;
                    }
                }

                return bestItem;
            }

            public static int count<T>(this IEnumerable<T> source)
            {
                if (source is ICollection<T> collection) return collection.Count;
                int total = 0;
                foreach (T _ in source) total++;
                return total;
            }

            public static int count<T>(this IEnumerable<T> source, Func<T, bool> predicate)
            {
                int total = 0;
                foreach (T item in source)
                {
                    if (predicate(item)) total++;
                }

                return total;
            }

            public static bool any<T>(this IEnumerable<T> source)
            {
                using IEnumerator<T> enumerator = source.GetEnumerator();
                return enumerator.MoveNext();
            }

            public static bool any<T>(this IEnumerable<T> source, Func<T, bool> predicate)
            {
                foreach (T item in source)
                {
                    if (predicate(item)) return true;
                }

                return false;
            }

            public static bool all<T>(this IEnumerable<T> source, Func<T, bool> predicate)
            {
                foreach (T item in source)
                {
                    if (!predicate(item)) return false;
                }

                return true;
            }

            public static T? find<T>(this IEnumerable<T> source, Func<T, bool> predicate)
            {
                foreach (T item in source)
                {
                    if (predicate(item)) return item;
                }

                return default;
            }

            public static T? findValue<T>(IEnumerable<T> source, Func<T, bool> predicate) where T : struct
            {
                foreach (T item in source)
                {
                    if (predicate(item)) return item;
                }

                return null;
            }

            public static T find<T>(this IEnumerable<T> source, Func<T, bool> predicate, T fallback)
            {
                foreach (T item in source)
                {
                    if (predicate(item)) return item;
                }

                return fallback;
            }

            public static int findIndex<T>(this IEnumerable<T> source, Func<T, bool> predicate)
            {
                int index = 0;
                foreach (T item in source)
                {
                    if (predicate(item)) return index;
                    index++;
                }

                return -1;
            }

            public static int findLastIndex<T>(this IEnumerable<T> source, Func<T, bool> predicate)
            {
                int found = -1;
                int index = 0;
                foreach (T item in source)
                {
                    if (predicate(item)) found = index;
                    index++;
                }

                return found;
            }

            public static T[] sort<T>(this IEnumerable<T> source)
            {
                T[] items = source.ToArray();
                if (typeof(T) == typeof(int) || typeof(T) == typeof(long) || typeof(T) == typeof(char) || typeof(T) == typeof(bool)
                    || typeof(T) == typeof(short) || typeof(T) == typeof(byte) || typeof(T) == typeof(uint) || typeof(T) == typeof(ulong))
                {
                    Array.Sort(items);
                    return items;
                }

                return sortStable(items, __PscpOrder<T>.Asc);
            }

            public static T[] sortBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector)
            {
                T[] items = source.ToArray();
                TKey[] keys = new TKey[items.Length];
                for (int i = 0; i < items.Length; i++) keys[i] = selector(items[i]);
                IComparer<TKey> comparer = __PscpOrder<TKey>.Asc;
                int[] order = new int[items.Length];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Array.Sort(order, (left, right) =>
                {
                    int compared = comparer.Compare(keys[left], keys[right]);
                    return compared != 0 ? compared : left.CompareTo(right);
                });
                T[] result = new T[items.Length];
                for (int i = 0; i < order.Length; i++) result[i] = items[order[i]];
                return result;
            }

            public static T[] sortWith<T>(this IEnumerable<T> source, Func<T, T, int> comparer) => sortStable(source.ToArray(), Comparer<T>.Create((left, right) => comparer(left, right)));
            public static T[] sortWith<T>(this IEnumerable<T> source, IComparer<T> comparer) => sortStable(source.ToArray(), comparer);

            private static T[] sortStable<T>(T[] items, IComparer<T> comparer)
            {
                int[] order = new int[items.Length];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Array.Sort(order, (left, right) =>
                {
                    int compared = comparer.Compare(items[left], items[right]);
                    return compared != 0 ? compared : left.CompareTo(right);
                });
                T[] result = new T[items.Length];
                for (int i = 0; i < order.Length; i++) result[i] = items[order[i]];
                return result;
            }

            public static int lowerBound<T>(this IReadOnlyList<T> source, T value) => lowerBound(source, value, __PscpOrder<T>.Asc);

            public static int lowerBound<T>(this IReadOnlyList<T> source, T value, IComparer<T> comparer)
            {
                int low = 0;
                int high = source.Count;
                while (low < high)
                {
                    int middle = low + ((high - low) >> 1);
                    if (comparer.Compare(source[middle], value) < 0) low = middle + 1;
                    else high = middle;
                }

                return low;
            }

            public static int upperBound<T>(this IReadOnlyList<T> source, T value) => upperBound(source, value, __PscpOrder<T>.Asc);

            public static int upperBound<T>(this IReadOnlyList<T> source, T value, IComparer<T> comparer)
            {
                int low = 0;
                int high = source.Count;
                while (low < high)
                {
                    int middle = low + ((high - low) >> 1);
                    if (comparer.Compare(source[middle], value) <= 0) low = middle + 1;
                    else high = middle;
                }

                return low;
            }

            public static bool chmin(ref int target, int value) { if (value < target) { target = value; return true; } return false; }
            public static bool chmin(ref long target, long value) { if (value < target) { target = value; return true; } return false; }
            public static bool chmin(ref double target, double value) { if (value < target) { target = value; return true; } return false; }
            public static bool chmin(ref char target, char value) { if (value < target) { target = value; return true; } return false; }
            public static bool chmax(ref int target, int value) { if (value > target) { target = value; return true; } return false; }
            public static bool chmax(ref long target, long value) { if (value > target) { target = value; return true; } return false; }
            public static bool chmax(ref double target, double value) { if (value > target) { target = value; return true; } return false; }
            public static bool chmax(ref char target, char value) { if (value > target) { target = value; return true; } return false; }

            public static bool chmin<T>(ref T target, T value)
            {
                if (__PscpOrder<T>.Asc.Compare(value, target) < 0)
                {
                    target = value;
                    return true;
                }

                return false;
            }

            public static bool chmax<T>(ref T target, T value)
            {
                if (__PscpOrder<T>.Asc.Compare(value, target) > 0)
                {
                    target = value;
                    return true;
                }

                return false;
            }

            public static T[] distinct<T>(this IEnumerable<T> source)
            {
                HashSet<T> seen = new();
                List<T> result = new();
                foreach (T item in source)
                {
                    if (seen.Add(item)) result.Add(item);
                }

                return result.ToArray();
            }

            public static T[] reverse<T>(this IEnumerable<T> source)
            {
                T[] result = source.ToArray();
                Array.Reverse(result);
                return result;
            }

            public static T[] copy<T>(this IEnumerable<T> source) => source.ToArray();

            public static Dictionary<T, int> freq<T>(this IEnumerable<T> source) where T : notnull
            {
                Dictionary<T, int> result = new();
                foreach (T item in source)
                {
                    result[item] = result.TryGetValue(item, out int count) ? count + 1 : 1;
                }

                return result;
            }

            public static Dictionary<T, int> groupCount<T>(this IEnumerable<T> source) where T : notnull => freq(source);

            public static Dictionary<T, int> index<T>(this IEnumerable<T> source) where T : notnull
            {
                Dictionary<T, int> result = new();
                int index = 0;
                foreach (T item in source)
                {
                    result.TryAdd(item, index);
                    index++;
                }

                return result;
            }
        }
        """;
}
