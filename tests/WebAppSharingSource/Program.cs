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
        using var window = new Form { Text = args[0], StartPosition = FormStartPosition.Manual,
            Location = new(40, 40), ClientSize = new(720, 480), BackColor = Color.DarkSlateBlue };
        window.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Text = "Synthetic sharing target\nNo accounts or user data", ForeColor = Color.White, Font = font });
        window.Shown += (_, _) => File.WriteAllText(args[1], "ready");
        Application.Run(window);
        return 0;
    }
}
