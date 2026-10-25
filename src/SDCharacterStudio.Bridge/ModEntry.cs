using StardewModdingAPI;
using SDCharacterStudio.Runtime.Api;

namespace SDCharacterStudio.Bridge;

public sealed class ModEntry : Mod
{
    private BridgeCoordinator? bridge;
    public override void Entry(IModHelper helper)
    {
        var config = helper.ReadConfig<BridgeConfig>();
        helper.Events.GameLoop.GameLaunched += (_, _) => Safe(() =>
        {
            var runtime = helper.ModRegistry.GetApi<ISdcsApi>("Player.SDCharacterStudio") ?? throw new InvalidOperationException("SDCS Runtime API is unavailable.");
            bridge = new(helper, Monitor, runtime, config);
            StardewValley.GameRunner.instance.Exiting += (_, _) => bridge?.Dispose();
            bridge.ExportOnce("static");
        });
        helper.Events.GameLoop.UpdateTicked += (_, e) => Safe(() => bridge?.Update(e.Ticks));
        helper.Events.GameLoop.SaveLoaded += (_, _) => Safe(() => { if (config.AutoExportOnSaveLoaded) bridge?.ScheduleSaveExport(); });
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => Safe(() => bridge?.CancelWorldWork());
        Add("sdcs_export", "Export live catalog: sdcs_export [static|save|all]", args => bridge!.Export(args.FirstOrDefault() ?? (Context.IsWorldReady ? "all" : "static")));
        Add("sdcs_export_asset", "Export one texture: sdcs_export_asset <assetName>", args => bridge!.ReExportAsset(args.Length == 1 ? args[0] : throw new ArgumentException("Supply one asset name.")));
        Add("sdcs_bridge_status", "Show preview and export status.", _ => Monitor.Log(bridge?.Status() ?? "Bridge is starting.", LogLevel.Info));
        void Add(string name, string description, Action<string[]> action) => helper.ConsoleCommands.Add(name, description, (_, args) => Safe(() => action(args)));
        Monitor.Log($"SDCharacterStudio.Bridge {ModManifest.Version} loaded.", LogLevel.Info);
    }
    private void Safe(Action action) { try { action(); } catch (Exception ex) { Monitor.Log($"Bridge: {ex.Message}", LogLevel.Error); } }
}
