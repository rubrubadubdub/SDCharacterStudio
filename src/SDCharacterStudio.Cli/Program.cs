using SDCharacterStudio.Editor.Export;

namespace SDCharacterStudio.Cli;

/// <summary>sdcs check / sdcs export: see <see cref="CommandLine.Usage"/>.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => CommandLine.Run(args, Console.Out, Console.Error);
}
