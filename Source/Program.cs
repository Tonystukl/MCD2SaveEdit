namespace MCD2SaveEdit;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args.Skip(1).ToArray());
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        return 0;
    }
}
