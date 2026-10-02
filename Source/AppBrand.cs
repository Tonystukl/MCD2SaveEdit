using System.Reflection;

namespace MCD2SaveEdit;

public static class AppBrand
{
    public static readonly Icon Icon = LoadIcon();
    static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCD2SaveEdit.Data.App.app.ico")
            ?? throw new InvalidDataException("The application icon is missing.");
        using var source = new Icon(stream, new Size(32, 32));
        return (Icon)source.Clone();
    }
}
