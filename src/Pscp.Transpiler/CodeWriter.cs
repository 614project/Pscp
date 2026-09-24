using System.Text;

namespace Pscp.Transpiler;

internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    public void Indent() => _indent++;

    public void Unindent() => _indent = Math.Max(0, _indent - 1);

    public void WriteLine(string text = "")
    {
        if (text.Length > 0)
        {
            _builder.Append(' ', _indent * 4);
            _builder.Append(text);
        }

        _builder.AppendLine();
    }

    public int Length => _builder.Length;

    public string TextFrom(int start) => _builder.ToString(start, _builder.Length - start);

    public void Truncate(int length) => _builder.Length = length;

    public override string ToString() => _builder.ToString();
}
