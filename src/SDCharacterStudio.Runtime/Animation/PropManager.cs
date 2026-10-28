using Microsoft.Xna.Framework;
using SDCharacterStudio.Core.Models;
using StardewValley;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>
/// Items a scene puts on the map (Prop beats, D-50): each drawn as the game draws the item (its texture and full source
/// rectangle, so furniture shows whole), standing on its tile and sorted with the actors by its bottom edge. They are
/// sprites, not placed objects: nothing is added to the location's objects or furniture, and they go when the scene is over.
/// </summary>
public sealed class PropManager
{
    private sealed record Placed(GameLocation Location,TemporaryAnimatedSprite Sprite,string Item,int X,int Y);
    private readonly List<Placed> placed=new();

    public int Count=>placed.Count;

    /// <param name="at">The beat's tile as a map tile (a place's spot found, D-52).</param>
    public void Apply(PropBeat beat,GameLocation? location,Position? at=null)
    {
        at??=beat.At;
        if(at?.Tile is not {Length:2} tile)throw new ArgumentException("A prop needs an At tile.");
        if(location==null)throw new InvalidOperationException("A prop needs a current location.");
        string? item=beat.Item==null?null:ItemRegistry.QualifyItemId(beat.Item)??beat.Item;
        if(beat.Remove)
        {
            foreach(var prop in placed.Where(p=>p.X==tile[0]&&p.Y==tile[1]&&(item==null||string.Equals(p.Item,item,StringComparison.OrdinalIgnoreCase))).ToArray())Remove(prop);
            return;
        }
        var data=ItemRegistry.GetDataOrErrorItem(item??throw new ArgumentException("A prop needs an item."));
        var source=data.GetSourceRect();
        int[] px=at.Px is {Length:2} offset?offset:new[]{0,0};
        // The picture's bottom-left corner on the bottom-left of the tile (as the stage draws it), in world pixels (4 per sprite pixel).
        var position=new Vector2(tile[0]*64+px[0]*4,(tile[1]+1)*64-source.Height*4+px[1]*4);
        // NPCs and the player sort by their standing pixel (tile row * 64 + 32): just in front of anyone on its row, behind the row below.
        float depth=Math.Max(0,(tile[1]*64+48+px[1]*4)/10000f);
        var sprite=new TemporaryAnimatedSprite("",source,999999f,1,999999,position,false,false,depth,0f,Color.White,4f,0f,0f,0f){texture=data.GetTexture()};
        location.temporarySprites.Add(sprite);
        placed.Add(new(location,sprite,data.QualifiedItemId,tile[0],tile[1]));
    }

    public void Clear(){foreach(var prop in placed.ToArray())Remove(prop);}

    private void Remove(Placed prop){prop.Location.temporarySprites.Remove(prop.Sprite);placed.Remove(prop);}
}
