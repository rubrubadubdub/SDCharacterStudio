using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SDCharacterStudio.Runtime.Media;

/// <summary>Play pack-owned PNG frames inside a fading, letterboxed film frame.</summary>
internal sealed class VideoMenu : IClickableMenu
{
    private const float ScreenFraction = 0.8f;
    private const int SideBorder = 3;
    private const int LetterboxBar = 10;
    private static readonly VideoFrameCache<Texture2D> FrameCache = new();
    private Texture2D[] frames;
    private readonly VideoPlayback playback;
    private readonly IMonitor monitor;
    private int scale = 1;
    private Rectangle area;
    private bool closed;

    /// <summary>Load a video from <c>videos/&lt;videoId&gt;/</c> in its owning content pack.</summary>
    public static VideoMenu? TryLoad(IContentPack pack, IMonitor monitor, string videoId)
    {
        if (string.IsNullOrEmpty(videoId) || !Regex.IsMatch(videoId, @"^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant))
        {
            monitor.Log($"Can't play video '{videoId}': invalid video ID.", LogLevel.Warn);
            return null;
        }

        try
        {
            string folder = $"videos/{videoId}";
            // The same rooted reader used for validation rejects links and paths outside the pack.
            var files = new DiskPackFileSystem(pack.DirectoryPath);
            string[] paths = VideoPlayback.OrderFramePaths(files.ListFiles(folder + "/*.png"));
            if (paths.Length == 0)
            {
                monitor.Log($"Can't play video '{videoId}' in '{pack.Manifest.UniqueID}': it has no numbered PNG frames.", LogLevel.Warn);
                return null;
            }

            VideoSettings settings = pack.ReadJsonFile<VideoSettings>(folder + "/video.json") ?? new VideoSettings();
            // ModContent PNG loads allocate uncached textures; our pack cache owns their lifetime.
            Texture2D[] frames = FrameCache.GetOrLoad(pack.Manifest.UniqueID, videoId, paths,
                path => pack.ModContent.Load<Texture2D>(path),
                loaded =>
                {
                    if (loaded.Any(frame => frame.Width != loaded[0].Width || frame.Height != loaded[0].Height))
                        throw new InvalidOperationException("All video frames must have the same dimensions.");
                });
            return new VideoMenu(frames, settings, monitor);
        }
        catch (Exception ex)
        {
            monitor.Log($"Can't load video '{videoId}' in '{pack.Manifest.UniqueID}': {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    /// <summary>Dispose cached frames for one pack, or all packs. Close all referencing menus before invalidating.</summary>
    public static void ClearPackCache(string? packId = null) => FrameCache.Clear(packId);

    private VideoMenu(Texture2D[] frames, VideoSettings settings, IMonitor monitor) : base(0, 0, 0, 0)
    {
        this.frames = frames;
        this.monitor = monitor;
        playback = new VideoPlayback(frames.Length, settings);
        UpdateLayout();
    }

    public override void update(GameTime time)
    {
        if (closed) return;
        foreach (string cue in playback.Advance(time.ElapsedGameTime.TotalMilliseconds))
        {
            try { Game1.playSound(cue); }
            catch (Exception ex) { monitor.Log($"Can't play video sound '{cue}': {ex.Message}", LogLevel.Warn); }
        }
        if (playback.IsFinished) CloseForCleanup();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key)) playback.SkipToEnd();
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button is Buttons.B or Buttons.Back) playback.SkipToEnd();
    }

    // Match the original video menu: accidental clicks do not dismiss a cutaway.
    public override void receiveLeftClick(int x, int y, bool playSound = true) { }
    public override void receiveRightClick(int x, int y, bool playSound = true) { }
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => UpdateLayout();

    public override void draw(SpriteBatch b)
    {
        if (closed) return;
        float fade = playback.Fade;
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.8f * fade);
        var film = new Rectangle(area.X - SideBorder * scale, area.Y - LetterboxBar * scale,
            area.Width + SideBorder * 2 * scale, area.Height + LetterboxBar * 2 * scale);
        b.Draw(Game1.staminaRect, film, Color.Black * fade);
        b.Draw(frames[playback.FrameIndex], area, Color.White * fade);
    }

    /// <summary>Release this menu during normal exit or event cleanup without closing another menu.</summary>
    public void CloseForCleanup()
    {
        if (closed) return;
        if (ReferenceEquals(Game1.activeClickableMenu, this)) exitThisMenu(playSound: false);
        else ReleaseFrames();
    }

    protected override void cleanupBeforeExit()
    {
        ReleaseFrames();
        base.cleanupBeforeExit();
    }

    private void ReleaseFrames()
    {
        closed = true;
        frames = Array.Empty<Texture2D>();
    }

    private void UpdateLayout()
    {
        if (closed) return;
        Texture2D first = frames[0];
        int maxWidth = (int)(Game1.uiViewport.Width * ScreenFraction);
        int maxHeight = (int)(Game1.uiViewport.Height * ScreenFraction);
        scale = Math.Max(1, Math.Min(maxWidth / (first.Width + SideBorder * 2), maxHeight / (first.Height + LetterboxBar * 2)));
        int width = first.Width * scale, height = first.Height * scale;
        area = new Rectangle((Game1.uiViewport.Width - width) / 2, (Game1.uiViewport.Height - height) / 2, width, height);
    }
}
