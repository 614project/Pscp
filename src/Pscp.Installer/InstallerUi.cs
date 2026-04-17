using System.Drawing;
using System.Windows.Forms;

namespace Pscp.Installer;

internal static class InstallerUiHost
{
    public static Task<int> RunAsync(string title, Func<IInstallerStatusSink, Task<int>> operation)
    {
        TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread uiThread = new(() => RunOnUiThread(title, operation, completion))
        {
            IsBackground = false,
            Name = "PSCP Installer UI",
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        return completion.Task;
    }

    private static void RunOnUiThread(string title, Func<IInstallerStatusSink, Task<int>> operation, TaskCompletionSource<int> completion)
    {
        Application.EnableVisualStyles();
        using InstallerProgressForm form = new(title);
        form.FormClosed += (_, _) => completion.TrySetResult(form.ExitCode);
        form.Shown += (_, _) => _ = ExecuteOperationAsync(form, operation);
        Application.Run(form);
    }

    private static async Task ExecuteOperationAsync(InstallerProgressForm form, Func<IInstallerStatusSink, Task<int>> operation)
    {
        try
        {
            int exitCode = await operation(form);
            form.Complete(
                success: exitCode == 0,
                message: exitCode == 0 ? "Operation completed successfully." : "Operation finished with errors.",
                exitCode);
        }
        catch (Exception ex)
        {
            form.Error(ex.Message);
            form.Complete(success: false, message: ex.Message, exitCode: 1);
        }
    }
}

internal sealed class InstallerProgressForm : Form, IInstallerStatusSink
{
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly TextBox _logBox;
    private readonly Button _closeButton;
    private readonly System.Windows.Forms.Timer _autoCloseTimer;
    private bool _allowClose;

    public InstallerProgressForm(string title)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(640, 360);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(248, 249, 251);
        Padding = new Padding(16);

        _titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = title,
            Location = new Point(16, 16),
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Text = "Preparing...",
            Location = new Point(16, 48),
            Size = new Size(608, 24),
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(16, 80),
            Size = new Size(608, 18),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25,
        };

        _logBox = new TextBox
        {
            Location = new Point(16, 112),
            Size = new Size(608, 184),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
        };

        _closeButton = new Button
        {
            Text = "Close",
            Enabled = false,
            Size = new Size(96, 32),
            Location = new Point(528, 308),
        };
        _closeButton.Click += (_, _) => Close();

        _autoCloseTimer = new System.Windows.Forms.Timer { Interval = 1400 };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
        };

        Controls.AddRange([_titleLabel, _statusLabel, _progressBar, _logBox, _closeButton]);
        FormClosing += OnFormClosing;
    }

    public int ExitCode { get; private set; } = 1;

    public void Info(string message)
        => Post(message, isWarning: false, isError: false);

    public void Warning(string message)
        => Post(message, isWarning: true, isError: false);

    public void Error(string message)
        => Post(message, isWarning: false, isError: true);

    public void Complete(bool success, string message, int exitCode)
    {
        ExitCode = exitCode;
        InvokeOnUiThread(() =>
        {
            _allowClose = true;
            _statusLabel.Text = message;
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Value = success ? 100 : 0;
            _closeButton.Enabled = true;
            AppendLog(success ? message : "Error: " + message);
            if (success)
            {
                _autoCloseTimer.Start();
            }
        });
    }

    private void Post(string message, bool isWarning, bool isError)
    {
        InvokeOnUiThread(() =>
        {
            _statusLabel.Text = message;
            AppendLog(isError ? "Error: " + message : isWarning ? "Warning: " + message : message);
        });
    }

    private void AppendLog(string message)
    {
        if (_logBox.TextLength > 0)
        {
            _logBox.AppendText(Environment.NewLine);
        }

        _logBox.AppendText(message);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void InvokeOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
    }
}
