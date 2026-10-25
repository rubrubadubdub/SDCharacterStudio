using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Rendering;
using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Runtime.Animation;

public sealed class EffectManager
{
    private const int MaxCount = 64, MaxBurstSprites = 256, MaxActiveSprites = 512;
    private static int nextSpriteId = -1200000000;
    private readonly IMonitor monitor;
    private readonly Func<string, ComposedPose> getCustomPose;
    private readonly Func<string, Pose> getDefinition;
    private readonly List<ActiveEffect> active = new();
    private readonly List<ActiveEffect> updateBuffer = new();
    private readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);
    private int sweatSequence;

    public EffectManager(IMonitor monitor, Func<string, ComposedPose> getCustomPose, Func<string, Pose> getDefinition)
    {
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        this.getCustomPose = getCustomPose ?? throw new ArgumentNullException(nameof(getCustomPose));
        this.getDefinition = getDefinition ?? throw new ArgumentNullException(nameof(getDefinition));
    }
    public bool UsesTexture(Texture2D texture)=>active.Any(x=>ReferenceEquals(x.Sprite.texture,texture)&&x.Location.temporarySprites.Contains(x.Sprite));

    /// <summary>World is the effect origin; actor supplies its standing depth, not a position override.</summary>
    public void Spawn(string id, GameLocation location, Vector2 world, Character? actor = null, int count = 1, string? color = null, int? duration = null, bool mirror = false)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Effect ID is required.", nameof(id));
        if (!float.IsFinite(world.X) || !float.IsFinite(world.Y)) throw new ArgumentOutOfRangeException(nameof(world));
        count = Math.Clamp(count, 0, MaxCount);
        if (count == 0) return;
        int? lifetime = duration.HasValue ? Math.Clamp(duration.Value, 1, 60000) : null;
        var tint = ParseColor(color);
        float depth = (actor?.StandingPixel.Y ?? world.Y + 64) / 10000f + 0.0002f;
        switch (id.ToLowerInvariant())
        {
            case "dust":
                SpawnDust(location, world, count, tint, lifetime, mirror);
                return;
            case "steam":
                for (int i = 0; i < Math.Min(count * 10, MaxBurstSprites); i++)
                {
                    var puff = TemporaryAnimatedSprite.GetTemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(372, 1956, 10, 10),
                        world + new Vector2(Game1.random.Next(-20, 21), Game1.random.Next(-4, 5)), false, 0.006f, tint ?? new Color(255, 90, 90));
                    puff.alpha = 0.85f;
                    puff.motion = new Vector2((i % 2 == 0 ? -1 : 1) * (0.3f + Game1.random.Next(0, 4) / 10f), -1.4f);
                    if (mirror) puff.motion.X = -puff.motion.X;
                    puff.flipped = mirror;
                    puff.acceleration = new Vector2(0, 0.012f);
                    puff.interval = 999999f;
                    puff.layerDepth = 1f;
                    puff.scale = 2f;
                    puff.scaleChange = 0.025f;
                    puff.rotationChange = Game1.random.Next(-5, 6) * MathF.PI / 256f;
                    puff.delayBeforeAnimationStart = i % 10 * 110;
                    Track(location, puff, lifetime ?? 4000, lifetime.HasValue);
                }
                return;
            case "sweat-drop":
                for (int i = 0; i < count; i++)
                {
                    bool front = sweatSequence++ % 2 == 0;
                    float direction = (front ? -1 : 1) * (mirror ? -1 : 1);
                    var drop = Generated("sweat-drop", new[] { ".b.", "bbb", "bwb", ".b." }, location,
                        world + new Vector2(direction * 16, 0), tint ?? Color.White, 4f, 0.02f, depth, mirror);
                    drop.motion = new Vector2(direction * 1.4f, -2.4f);
                    drop.acceleration = new Vector2(0, 0.15f);
                    Track(location, drop, lifetime ?? 1200, lifetime.HasValue);
                }
                return;
            case "sparkle":
            case "heart-burst":
            case "tears":
                SpawnDesigned(id.ToLowerInvariant(), location, world, count, tint, lifetime, depth, mirror);
                return;
            default:
                SpawnCustom(id, location, world, count, tint ?? Color.White, lifetime, depth, mirror);
                return;
        }
    }

    /// <summary>Native sprites advance their own motion/fade; only custom strip frames are selected here.</summary>
    public void Update(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        // Frame cues can append dust, and the active cap can evict older entries.
        // Iterate a snapshot so new particles are not aged by this same update.
        updateBuffer.Clear();
        updateBuffer.AddRange(active);
        for (int i=updateBuffer.Count-1;i>=0;i--)
        {
            var effect=updateBuffer[i];
            // Pooled native sprites reset id before reuse. Never remove another effect's recycled sprite.
            if (effect.Sprite.id != effect.Token || !effect.Location.temporarySprites.Contains(effect.Sprite))
            {
                active.Remove(effect);
                continue;
            }
            double advance = Math.Min(elapsed, Math.Max(0, effect.Duration - effect.Elapsed));
            effect.Elapsed += elapsed;
            if (effect.Timeline is { } timeline)
            {
                var entered=timeline.Advance(advance);
                for(int frame=0;frame<entered.Count;frame++)EmitFrameCue(effect, entered[frame]);
                effect.Sprite.sourceRect = new Rectangle(timeline.Frame * effect.Width, 0, effect.Width, effect.Height);
            }
            if (effect.Elapsed >= effect.Duration)
            {
                Remove(effect);
                active.Remove(effect);
            }
        }
    }

    /// <summary>Remove all referencing sprites first; only generated bitmap textures are owned here.</summary>
    public void Clear()
    {
        foreach (var effect in active) Remove(effect);
        active.Clear();
        updateBuffer.Clear();
        foreach (var texture in textures.Values) texture.Dispose();
        textures.Clear();
        sweatSequence = 0;
    }

    private void SpawnDust(GameLocation location, Vector2 world, int count, Color? tint, int? duration, bool mirror)
    {
        var before = new HashSet<TemporaryAnimatedSprite>(location.temporarySprites);
        try
        {
            for (int i = 0; i < count; i++)
                Utility.addDirtPuffs(location, (int)MathF.Floor(world.X / 64), (int)MathF.Floor(world.Y / 64), 1, 1, 3);
        }
        finally
        {
            // Utility owns its implementation; capture every sprite it actually added, even on partial failure.
            foreach (var sprite in location.temporarySprites.Where(sprite => !before.Contains(sprite)).ToArray())
            {
                if (tint is { } value) sprite.color = value;
                if (mirror) { sprite.flipped = !sprite.flipped; sprite.motion.X = -sprite.motion.X; }
                Track(location, sprite, duration ?? 3000, duration.HasValue, alreadyAdded: true);
            }
        }
    }

    private void SpawnDesigned(string id, GameLocation location, Vector2 world, int count, Color? tint, int? duration, float depth, bool mirror)
    {
        int particles = Math.Min(count * (id == "heart-burst" ? 6 : id == "tears" ? 2 : 1), MaxBurstSprites);
        string[] pattern = id switch
        {
            "heart-burst" => new[] { ".rr.rr.", "rrrrrrr", "rrrrrrr", ".rrrrr.", "..rrr..", "...r..." },
            "tears" => new[] { ".b", "bw", "bb" },
            _ => new[] { "..w..", "..y..", "wywyw", "..y..", "..w.." }
        };
        for (int i = 0; i < particles; i++)
        {
            float direction = (i % 2 == 0 ? -1 : 1) * (mirror ? -1 : 1);
            var sprite = Generated(id, pattern, location, world + new Vector2(direction * (id == "tears" ? 10 : i % 3 * 8), 0),
                tint ?? Color.White, id == "heart-burst" ? 3 : 4, id == "tears" ? 0.03f : 0.02f, depth, mirror);
            sprite.motion = id == "tears" ? new Vector2(direction * 0.25f, 1.6f) : new Vector2(direction * (0.4f + i % 3 * 0.35f), -1.8f - i % 2 * 0.3f);
            sprite.acceleration = id == "tears" ? new Vector2(0, 0.12f) : new Vector2(0, 0.02f);
            if (id == "sparkle")
            {
                sprite.scaleChange = 0.015f;
                sprite.rotationChange = 0.04f;
            }
            Track(location, sprite, duration ?? 1200, duration.HasValue);
        }
    }

    private void SpawnCustom(string id, GameLocation location, Vector2 world, int count, Color tint, int? duration, float depth, bool mirror)
    {
        var pose = getCustomPose(id);
        var definition = getDefinition(id);
        if (pose.Texture.IsDisposed || pose.Count <= 0 || pose.FrameWidth <= 0 || pose.FrameHeight <= 0
            || definition.Frames.Count != pose.Count || (long)pose.FrameWidth * pose.Count > pose.Texture.Width || pose.FrameHeight > pose.Texture.Height)
            throw new ArgumentException($"Custom effect '{id}' has no usable frame strip.", nameof(id));
        var firstTimeline = new PoseTimeline(definition, "once");
        var sequence = definition.Intro.Concat(definition.Loop).ToArray();
        if (sequence.Length == 0) sequence = Enumerable.Range(0, definition.Frames.Count).ToArray();
        double lifetime = duration ?? sequence.Sum(index => (double)definition.Frames[index].Duration);
        for (int i = 0; i < count; i++)
        {
            var timeline = i == 0 ? firstTimeline : new PoseTimeline(definition, "once");
            var sprite = new TemporaryAnimatedSprite("", new Rectangle(timeline.Frame * pose.FrameWidth, 0, pose.FrameWidth, pose.FrameHeight), 999999f, 1, 999999,
                world + new Vector2((i - (count - 1) / 2f) * 8, 0), false, mirror, depth, 0f, tint, 4f, 0f, 0f, 0f) { texture = pose.Texture };
            var effect = Track(location, sprite, lifetime, false, timeline, pose.FrameWidth, pose.FrameHeight, definition: definition);
            EmitFrameCue(effect, timeline.Frame);
        }
    }

    private void EmitFrameCue(ActiveEffect effect, int index)
    {
        var frame = effect.Definition!.Frames[index];
        if (frame.Sound != null && !CueSounds.Muted)
            effect.Location.playSound(frame.Sound);
        if (frame.Dust)
            SpawnDust(effect.Location, effect.Sprite.position, 1, null, null, effect.Sprite.flipped);
    }

    private TemporaryAnimatedSprite Generated(string id, string[] pattern, GameLocation location, Vector2 world, Color tint, float scale, float fade, float depth, bool mirror)
    {
        if (!textures.TryGetValue(id, out var texture))
        {
            var colors = new Color[pattern[0].Length * pattern.Length];
            for (int y = 0; y < pattern.Length; y++)
                for (int x = 0; x < pattern[y].Length; x++)
                    colors[y * pattern[0].Length + x] = pattern[y][x] switch
                    {
                        'b' => new Color(124, 196, 242), 'w' => Color.White,
                        'r' => new Color(255, 100, 150), 'y' => new Color(255, 226, 110), _ => Color.Transparent
                    };
            texture = new Texture2D(Game1.graphics.GraphicsDevice, pattern[0].Length, pattern.Length);
            try { texture.SetData(colors); }
            catch { texture.Dispose(); throw; }
            textures.Add(id, texture);
        }
        return new TemporaryAnimatedSprite("", new Rectangle(0, 0, texture.Width, texture.Height), 999999f, 1, 999999,
            world, false, mirror, depth, fade, tint, scale, 0f, 0f, 0f) { texture = texture };
    }

    private ActiveEffect Track(GameLocation location, TemporaryAnimatedSprite sprite, double duration, bool stretchFade, PoseTimeline? timeline = null, int width = 0, int height = 0, bool alreadyAdded = false, Pose? definition = null)
    {
        if (stretchFade && sprite.alphaFade > 0)
            sprite.alphaFade = (float)(sprite.alpha / Math.Max(1, (duration - sprite.delayBeforeAnimationStart) * 60 / 1000));
        while (active.Count >= MaxActiveSprites)
        {
            Remove(active[0]);
            active.RemoveAt(0);
        }
        sprite.id = System.Threading.Interlocked.Decrement(ref nextSpriteId);
        var effect = new ActiveEffect(location, sprite, duration, timeline, width, height, definition);
        active.Add(effect);
        if (!alreadyAdded) location.temporarySprites.Add(sprite);
        return effect;
    }

    private static void Remove(ActiveEffect effect)
    {
        if (effect.Sprite.id == effect.Token) effect.Location.temporarySprites.Remove(effect.Sprite);
    }

    private Color? ParseColor(string? value)
    {
        if (value is null) return null;
        if (value.StartsWith('#') && value.Length is 7 or 9 && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
        {
            if (value.Length == 7) return new Color((byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
            return new Color((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }
        monitor.Log($"Effect color '{value}' is not #RRGGBB or #RRGGBBAA; using its default color.", LogLevel.Warn);
        return null;
    }

    private sealed class ActiveEffect
    {
        public GameLocation Location { get; }
        public TemporaryAnimatedSprite Sprite { get; }
        public int Token { get; }
        public double Duration { get; }
        public PoseTimeline? Timeline { get; }
        public Pose? Definition { get; }
        public int Width { get; }
        public int Height { get; }
        public double Elapsed { get; set; }
        public ActiveEffect(GameLocation location, TemporaryAnimatedSprite sprite, double duration, PoseTimeline? timeline, int width, int height, Pose? definition)
        { Location = location; Sprite = sprite; Token = sprite.id; Duration = duration; Timeline = timeline; Width = width; Height = height; Definition = definition; }
    }
}
