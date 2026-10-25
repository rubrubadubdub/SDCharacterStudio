namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// Ready-made poses (D-48 stage 4): recipes over puppet parts, so the result stays live (it follows sprite mods). A
/// recipe moves parts and can trim the legs to the thighs (kneeling: the lower legs go out of sight and everything
/// above drops so the knees meet the ground). Offsets are in sprite pixels; "forward" follows the facing. Arm moves
/// apply where the arms are separate parts (front and back views). Only moves that read well on real art are here:
/// lifting a head opens a gap in long hair, and raising arms needs new pixels, so neither is a recipe.
/// </summary>
public static class PoseRecipes
{
    public sealed record Recipe(string Id, string Title, string Hint, double KeepThighs, Func<string, string, (int X, int Y)> Move);

    private static int Forward(string facing) => facing switch { "left" => -1, "right" => 1, _ => 0 };

    public static IReadOnlyList<Recipe> All { get; } = new[]
    {
        new Recipe("bow", "Bow the head", "Head down, a little forward.", 1,
            (part, facing) => part == "head" ? (Forward(facing), 2) : (0, 0)),
        new Recipe("kneel", "Kneel", "On the knees: the lower legs go out of sight.", 0.4,
            (part, facing) => (0, 0)),
        new Recipe("beg", "Beg", "Kneeling, head bowed, hands raised (front and back views).", 0.4,
            (part, facing) => part.EndsWith("arm") ? (0, -3) : part == "head" ? (Forward(facing), 1) : (0, 0)),
        new Recipe("shrug", "Shrug", "Head down a touch, shoulders up (front and back views).", 1,
            (part, facing) => part.EndsWith("arm") ? (0, -1) : part == "head" ? (0, 1) : (0, 0)),
    };

    public static Recipe? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}
