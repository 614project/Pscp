using System.Diagnostics;

namespace Pscp.Installer;

internal interface IInstallerStatusSink
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}

internal sealed class ConsoleInstallerStatusSink : IInstallerStatusSink
{
    public void Info(string message) => Write(message, Console.Out);

    public void Warning(string message) => Write("Warning: " + message, Console.Out);

    public void Error(string message) => Write("Error: " + message, Console.Error);

    private static void Write(string message, TextWriter writer)
    {
        try
        {
            writer.WriteLine(message);
        }
        catch
        {
            Trace.WriteLine(message);
        }
    }
}

internal sealed class NullInstallerStatusSink : IInstallerStatusSink
{
    public static readonly NullInstallerStatusSink Instance = new();

    private NullInstallerStatusSink()
    {
    }

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message)
    {
    }
}
