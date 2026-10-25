using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace SDCharacterStudio.Runtime.Loading;

public sealed class EventInjector
{
    private readonly CutsceneRegistry registry;
    private readonly IMonitor monitor;
    public EventInjector(CutsceneRegistry registry, IMonitor monitor) { this.registry = registry; this.monitor = monitor; }

    public void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        try
        {
            if (!e.NameWithoutLocale.StartsWith("Data/Events/")) return;
            var scenes = registry.All.Where(x => e.NameWithoutLocale.IsEquivalentTo("Data/Events/" + x.Compiled.Location)).ToArray();
            if (scenes.Length == 0) return;
            // Even a low-priority loader replaces a vanilla asset, so only supply the
            // empty base when that location has no installed vanilla events file.
            var vanillaAsset = Path.Combine(Constants.GamePath, "Content", e.NameWithoutLocale.Name.Replace('/', Path.DirectorySeparatorChar) + ".xnb");
            if (!File.Exists(vanillaAsset)) e.LoadFrom(() => new Dictionary<string, string>(), AssetLoadPriority.Low);
            e.Edit(asset =>
            {
                try
                {
                    var data = asset.AsDictionary<string, string>().Data;
                    foreach (var scene in scenes)
                    {
                        data[scene.Compiled.MainKey] = scene.Compiled.MainScript;
                        foreach (var script in scene.Compiled.SubScripts) data[script.Key] = script.Value;
                    }
                }
                catch (Exception ex) { monitor.Log($"Could not inject SDCS events into '{e.NameWithoutLocale}': {ex}", LogLevel.Error); }
            }, AssetEditPriority.Late);
        }
        catch (Exception ex) { monitor.Log($"Could not prepare SDCS event asset '{e.NameWithoutLocale}': {ex}", LogLevel.Error); }
    }
}
