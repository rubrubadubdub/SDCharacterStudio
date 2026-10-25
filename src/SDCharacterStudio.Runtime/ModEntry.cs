using StardewModdingAPI;

namespace SDCharacterStudio.Runtime;

/// <summary>The framework mod: one engine for every installed SDCS content pack, with console commands and config.</summary>
public sealed class ModEntry : Mod
{
    private SdcsHost host = null!;

    public override void Entry(IModHelper helper)
    {
        host = new SdcsHost(helper, Monitor, ModManifest, helper.ReadConfig<ModConfig>(), consoleCommands: true);
        Monitor.Log($"SDCharacterStudio {ModManifest.Version} loaded.", LogLevel.Info);
    }

    public override object GetApi() => host.Api;
}
