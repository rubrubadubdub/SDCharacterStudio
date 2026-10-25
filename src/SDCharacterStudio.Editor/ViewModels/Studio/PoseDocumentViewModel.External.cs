using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>"Edit in external editor" for paint and image layers (doc 07 §11).</summary>
public partial class PoseDocumentViewModel
{
    private readonly List<ExternalEditSession> externalSessions = new();

    /// <summary>The strip holds this layer in every frame that has it (paint is per frame), left to right.</summary>
    public ExternalEditSession? EditExternally(Layer layer, bool launch = true)
    {
        int layerIndex = Frame?.Layers.IndexOf(layer) ?? -1;
        if (layerIndex < 0) return null;
        // Which frames' layers are "this layer": the same slot (and type) in every frame.
        var frames = new List<(int Frame, string Path)>();
        for (int i = 0; i < Model.Frames.Count; i++)
        {
            var candidate = i == FrameIndex ? layer : Model.Frames[i].Layers.ElementAtOrDefault(layerIndex) is { } other && other.Type == layer.Type ? other : null;
            string? path = (candidate as PaintLayer)?.Image ?? (candidate as ImageLayer)?.Image;
            if (path != null && (layer is PaintLayer || i == FrameIndex)) frames.Add((i, path));
        }
        if (frames.Count == 0) return null;
        var cells = frames.Select(f => CellImage(f.Frame, layerIndex)).ToList();
        var strip = ExternalEditor.Strip(cells, Width, Height);
        var slotPalette = PaletteViewModel.SlotColors.ToDictionary(s => s.Name, s => StudioImages.Hex(s.Color));
        string name = $"{Model.Id}-{(layer is PaintLayer ? "paint" : "image")}-{layerIndex}";
        var sidecar = new ExternalSidecar(Studio.Shell.Document?.PackId ?? "", Model.Id ?? ItemId, LayerName(layer), Width, Height, frames.Count,
            slotPalette, $"Frames left to right: {string.Join(", ", frames.Select(f => f.Frame + 1))}. Keep the size. Load sdcs-slots.gpl to paint with the reserved slot colours.");
        var snapshot = frames.ToArray();
        var session = ExternalEditor.Start(name, strip, sidecar, Studio.Settings.ExternalEditor, image => Reimport(image, snapshot, layerIndex), Ui, launch);
        externalSessions.Add(session);
        if (launch) Ui.Toast("Editing outside", $"Saving {System.IO.Path.GetFileName(session.File)} in your editor brings the change back here.", ToastKind.Info);
        return session;
    }

    private Core.Compositing.RgbaImage? CellImage(int frameIndex, int layerIndex)
    {
        if (Model.Frames[frameIndex].Layers.ElementAtOrDefault(layerIndex) is not { } layer) return null;
        if (layer is PaintLayer paint) return Context.PackImage(paint.Image);
        var target = TargetForFrame(frameIndex, layerIndex);
        return target == null ? null : StudioImages.Crop(target.Image, target.CellX, target.CellY, target.CellWidth, target.CellHeight);
    }

    private PaintTarget? TargetForFrame(int frameIndex, int layerIndex)
    {
        if (frameIndex == FrameIndex) return TargetFor(layerIndex);
        var image = Model.Frames[frameIndex].Layers.ElementAtOrDefault(layerIndex) as ImageLayer;
        if (image?.Image == null || Context.PackImage(image.Image) is not { } sheet) return null;
        var size = image.FrameSize ?? new[] { Width, Height };
        int columns = Math.Max(1, sheet.Width / size[0]);
        return new PaintTarget(image.Image, sheet, image.Frame % columns * size[0], image.Frame / columns * size[1], size[0], size[1], 0, 0, false);
    }

    private void Reimport(Core.Compositing.RgbaImage strip, (int Frame, string Path)[] frames, int layerIndex)
    {
        var cells = ExternalEditor.Split(strip, Width, Height, frames.Length);
        if (cells == null) { Ui.Toast("Size changed", $"The strip must stay {Width * frames.Length}×{Height}. The change wasn't brought back.", ToastKind.Warning); return; }
        Save("Edit outside", _ => { }, lib =>
        {
            for (int i = 0; i < frames.Length; i++)
            {
                var layer = Model.Frames.ElementAtOrDefault(frames[i].Frame)?.Layers.ElementAtOrDefault(layerIndex);
                if (layer is PaintLayer paint && paint.Image == frames[i].Path) lib.WriteImage(paint.Image, cells[i]);
                else if (layer is ImageLayer image && image.Image == frames[i].Path && lib.ReadImage(image.Image) is { } sheet)
                {
                    var size = image.FrameSize ?? new[] { Width, Height };
                    int columns = Math.Max(1, sheet.Width / size[0]);
                    int x = image.Frame % columns * size[0], y = image.Frame / columns * size[1];
                    PixelTools.Clear(sheet, new PixelRect(x, y, size[0], size[1]));
                    PixelTools.Paste(sheet, cells[i], x, y);
                    lib.WriteImage(image.Image, sheet);
                }
            }
        }, rebuildFields: false);
        Context.InvalidatePack();
        Render(); RenderThumbnails();
    }

    private void CloseExternalSessions()
    {
        foreach (var session in externalSessions) session.Dispose();
        externalSessions.Clear();
    }
}
