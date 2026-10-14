namespace WidgetRail.Tests.WebAppSharingSource;

internal static class Program
{
    // Opaque ordinary desktop window, with only generated text. This process
    // supplies no browser, captures no content, and reads no user information.
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !Path.IsPathFullyQualified(args[1])) return 2;
        ApplicationConfiguration.Initialize();
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, 20);
        using var window = new SharingTargetForm { Text = args[0], StartPosition = FormStartPosition.Manual,
            Location = new(40, 40), ClientSize = new(720, 480), BackColor = Color.DarkSlateBlue };
        var label = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Text = "Synthetic sharing target\nNo accounts or user data", ForeColor = Color.White, Font = font };
        window.Controls.Add(label);
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var frame = 0;
        timer.Tick += (_, _) => label.Text = $"Synthetic sharing target\nNo accounts or user data\nFrame {++frame}";
        timer.Start();
        window.Shown += (_, _) => File.WriteAllText(args[1], "ready");
        Application.Run(window);
        return 0;
    }

    // A capture source must not steal foreground from the overlay being tested.
    private sealed class SharingTargetForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
}
