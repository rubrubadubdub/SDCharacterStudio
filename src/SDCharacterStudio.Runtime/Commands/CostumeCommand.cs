using SDCharacterStudio.Core.Models;
using StardewValley;
using GameEvent=StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class CostumeCommand(CommandServices services)
{
    public void Handle(GameEvent ev,string[] args,EventContext context)
    {
        Apply(ev,services.GetBeat<CostumeBeat>(args),false);
        ev.CurrentCommand++;
    }
    public void Apply(GameEvent ev,CostumeBeat beat,bool skipping)
    {
        var actor=beat.Actor ?? throw new ArgumentException("Costume requires an actor.");
        if(services.Animation.Actor(actor) is Farmer farmer)
        {
            if(beat.Portrait!=null)throw new ArgumentException("Costume.Portrait requires an NPC actor.");
            if(beat.Sprite!=null)throw new ArgumentException("Farmer costumes use Set or Preset; use Pose for a farmer stand-in.");
            if(beat.Restore)
            {services.Animation.Costumes.Restore(farmer);services.Animation.PartVisibility(farmer).Clear();}
            else
            {
                var values=new Dictionary<string,Newtonsoft.Json.Linq.JToken>(StringComparer.OrdinalIgnoreCase);
                if(beat.Preset!=null)foreach(var pair in services.Animation.CostumeValues(beat.Preset))values[pair.Key]=pair.Value;
                if(beat.Set!=null)foreach(var pair in beat.Set)values[pair.Key]=pair.Value;
                if(values.Count==0)throw new ArgumentException("Farmer costume requires Set, Preset or Restore.");
                services.Animation.Costumes.Apply(farmer,values);
            }
            return;
        }
        if(beat.Set?.Count>0 || beat.Preset!=null)throw new ArgumentException("Costume Set and Preset require a Farmer actor.");
        if(services.Animation.Actor(actor) is not NPC npc)throw new ArgumentException("Unsupported costume actor.");
        if(beat.Restore)
        {
            var visible=services.Animation.LogicalVisible(actor);
            services.Animation.Release(actor,false);
            services.Session.RestoreActor(npc);
            services.Animation.PortraitCostumes.Restore(npc);
            services.Animation.SetVisible(actor,visible);
        }
        else
        {
        // Prepare the portrait before changing either override so invalid portrait content is atomic.
        var portrait=beat.Portrait!=null?services.Animation.ComposePortrait(beat.Portrait):null;
        if(beat.Sprite?.Pose is {} id)
        {
            if(services.Animation.GetPose(id).Kind!="sheet") throw new ArgumentException("Costume Sprite.Pose must select a sheet pose.");
            string name=npc.Name.Any(char.IsWhiteSpace)?"\""+npc.Name+"\"":npc.Name;
            if(skipping)
            {npc.Sprite.LoadTexture("Characters/"+NPC.getTextureNameForCharacter(npc.Name)+"_"+services.Animation.SheetSuffix(id),syncTextureName:false);npc.spriteOverridden=true;}
            else ev.InsertNextCommand("changeSprite "+name+" "+services.Animation.SheetSuffix(id));
        }
        else if(beat.Sprite?.Asset is {} asset)
        {npc.Sprite.LoadTexture(asset,syncTextureName:false);npc.spriteOverridden=true;}
        else if(portrait==null)throw new ArgumentException("NPC costume requires Sprite.Asset, Sprite.Pose, Portrait or Restore.");
        if(portrait!=null)services.Animation.PortraitCostumes.Apply(npc,beat.Portrait!,portrait.Texture,services.Session.ActiveScene);
        }
    }
}
