using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;

namespace SdcsNativeMultiplayerDriver;

public interface IBenchApi
{
    bool PlayCutscene(string cutscene, bool checkConditions = false);
    bool IsPlaying { get; }
    void StopCutscene();
    IReadOnlyList<string> GetCutscenes();
    string EngineVersion { get; }
}

/// <summary>Real LAN transport with disposable in-memory host/cabin and native farmhand selection. Never saves.</summary>
public sealed class ModEntry : Mod
{
    private int ticks;
    private bool initialized, selected, failed;
    private bool wardrobeApplied;
    private string role = "";
    private FarmhandMenu? menu;
    private string At(string name) => Path.Combine(Helper.DirectoryPath, name);
    private IBenchApi Bench => Helper.ModRegistry.GetApi<IBenchApi>("Bench.SdcsNoCode") ?? throw new InvalidOperationException("Embedded bench API missing.");

    public override void Entry(IModHelper helper)
    {
        role = (string?)JObject.Parse(File.ReadAllText(At("role.json")))["Role"] ?? throw new InvalidDataException("Missing Role");
        Directory.CreateDirectory(At("requests")); Directory.CreateDirectory(At("responses"));
        helper.Events.GameLoop.Saving += (_, _) => throw new InvalidOperationException("The multiplayer fixture must never save.");
        helper.Events.GameLoop.UpdateTicked += (_, _) =>
        {
            if(failed)return;
            try { Tick(); }
            catch(Exception ex)
            {
                failed=true; Write("failed.json",new JObject { ["Error"]=ex.ToString() });
                Monitor.Log(ex.ToString(),LogLevel.Error);
            }
        };
    }

    private void Tick()
    {
        ticks++;
        if(!initialized && ticks>=120)
        {
            Game1.exitActiveMenu();
            if(role=="host") CreateHost();
            else
            {
                var client=Game1.Multiplayer.InitClient(new LidgrenClient("127.0.0.1"));
                menu=new FarmhandMenu(client); Game1.activeClickableMenu=menu;
                client.connect();
            }
            initialized=true;
        }
        if(!initialized)return;
        if(role=="client" && !selected && menu?.client.availableFarmhands is { Count: > 0 } hands)
        {
            var farmer=hands[0];
            Monitor.Log($"Selecting offered native farmhand {farmer.UniqueMultiplayerID}",LogLevel.Info);
            new FarmhandMenu.FarmhandSlot(menu,farmer).Activate(); selected=true;
        }
        if(Context.IsWorldReady)
        {
            Game1.options.pauseWhenOutOfFocus=false;
            Game1.timeOfDay=1200;
            if(Game1.CurrentEvent is { } foreign && !foreign.id.StartsWith("Bench.SdcsNoCode.",StringComparison.Ordinal))foreign.skipEvent();
        }
        if(ticks%10==0)
        {
            Write("state.json",State());
            foreach(var path in Directory.EnumerateFiles(At("requests"),"*.json").ToArray())
            {
                var request=JObject.Parse(File.ReadAllText(path));
                string id=Path.GetFileNameWithoutExtension(path);
                try { Write("responses/"+id+".json",new JObject { ["Ok"]=true,["Data"]=Execute((string?)request["Op"]??"") }); }
                catch(Exception ex) { Write("responses/"+id+".json",new JObject { ["Ok"]=false,["Error"]=ex.ToString() }); }
                File.Move(path,Path.ChangeExtension(path,".done"),true);
            }
        }
    }

    private void CreateHost()
    {
        Game1.game1.loadForNewGame();
        Game1.player.Name="M9Host"; Game1.player.farmName.Value="Disposable LAN";
        Game1.player.isCustomized.Value=true;
        Game1.dayOfMonth=1; Game1.timeOfDay=1200; Game1.gameMode=3;
        Game1.currentLocation=Game1.getLocationFromName("ScienceHouse");
        Game1.player.currentLocation=Game1.currentLocation; Game1.player.Position=new Vector2(16*64,9*64); Game1.player.CanMove=true;
        MarkEvents();
        Game1.multiplayerMode=2;
        var farm=Game1.getFarm();
        var building=new Building("Cabin",new Vector2(30,10));
        var cabin=new Cabin("Maps/"+Game1.buildingData["Cabin"].IndoorMap);
        cabin.uniqueName.Value="M9SmokeCabin"; cabin.isStructure.Value=true; cabin.ParentBuilding=building;
        building.indoors.Value=cabin; building.daysOfConstructionLeft.Value=0; farm.buildings.Add(building);
        cabin.CreateFarmhand();
        cabin.owner.Name="M9Farmhand"; cabin.owner.isCustomized.Value=true;
        foreach(var location in Game1.locations)
            if(location.TryGetLocationEvents(out _,out var events))foreach(var key in events.Keys)cabin.owner.eventsSeen.Add(key.Split('/')[0]);
        Game1.Multiplayer.StartServer();
        Game1.server.initializeHost();
        Monitor.Log($"Native LAN server started; cabin farmhand {cabin.owner.UniqueMultiplayerID}",LogLevel.Info);
    }

    private static void MarkEvents()
    {
        foreach(var location in Game1.locations)
            if(location.TryGetLocationEvents(out _,out var events))foreach(var key in events.Keys)Game1.player.eventsSeen.Add(key.Split('/')[0]);
    }
    private JObject Execute(string op)
    {
        switch(op)
        {
            case "state": return State();
            case "play":
                Game1.exitActiveMenu();
                return new JObject { ["Accepted"]=Bench.PlayCutscene("wave"),["Engine"]=Bench.EngineVersion };
            case "stop": Bench.StopCutscene(); return State();
            case "wardrobe": return ApplyWardrobeFixture();
            case "close-dialogue":
                if(Game1.activeClickableMenu is DialogueBox box){box.finishTyping();box.receiveLeftClick(0,0,false);}
                return State();
            default: throw new ArgumentException("Unknown operation "+op);
        }
    }
    private JObject ApplyWardrobeFixture()
    {
        var hairData=Game1.content.Load<Dictionary<int,string>>("Data/HairData");
        if(!hairData.TryGetValue(9000,out var hairRaw))throw new InvalidOperationException("Fixture HairData key 9000 is missing: " + string.Join(",",hairData.Keys.Where(x=>x>=9000).Take(8)));
        var hats=Game1.content.Load<Dictionary<string,string>>("Data/Hats");
        if(!hats.TryGetValue("Player.SDCS.M9TestHat",out var hatRaw))throw new InvalidOperationException("Fixture Data/Hats key is missing.");
        var metadata=Farmer.GetHairStyleMetadata(9000) ?? throw new InvalidOperationException("Farmer.GetHairStyleMetadata(9000) returned null.");
        var item=ItemRegistry.Create("(H)Player.SDCS.M9TestHat",1,allowNull:true) as StardewValley.Objects.Hat
            ?? throw new InvalidOperationException("Fixture hat Player.SDCS.M9TestHat is missing.");
        Game1.player.changeHairStyle(9000);
        Game1.player.hat.Value=item;
        wardrobeApplied=true;
        return new JObject
        {
            ["Applied"]=true,["Hair"]=Game1.player.hair.Value,["HairData"]=hairRaw,
            ["HairTexture"]=metadata.texture?.Name,["HairTileX"]=metadata.tileX,["HairTileY"]=metadata.tileY,
            ["HairUsesUniqueLeftSprite"]=metadata.usesUniqueLeftSprite,["HairCoveredIndex"]=metadata.coveredIndex,
            ["HairIsBaldStyle"]=metadata.isBaldStyle,["Hat"]=Game1.player.hat.Value.QualifiedItemId,["HatData"]=hatRaw,
            ["HatTexture"]=Game1.player.hat.Value.itemId.Value
        };
    }
    private JObject State() => new()
    {
        ["Role"]=role,["Tick"]=ticks,["WorldReady"]=Context.IsWorldReady,["PlayerFree"]=Context.IsPlayerFree,
        ["IsServer"]=Game1.IsServer,["IsClient"]=Game1.IsClient,["MultiplayerMode"]=Game1.multiplayerMode,
        ["PlayerId"]=Game1.player?.UniqueMultiplayerID,["PlayerName"]=Game1.player?.Name,
        ["OtherFarmers"]=new JArray(Game1.otherFarmers.Values.Select(f=>new JObject { ["Id"]=f.UniqueMultiplayerID,["Name"]=f.Name })),
        ["Event"]=Game1.CurrentEvent?.id,["EventUp"]=Game1.eventUp,["DialogueUp"]=Game1.dialogueUp,
        ["DisplayFarmer"]=Game1.displayFarmer,["Warping"]=Game1.isWarping,["LocationRequest"]=Game1.locationRequest?.Name,
        ["GlobalFade"]=Game1.globalFade,["FadeToBlack"]=Game1.fadeToBlack,
        ["Location"]=Game1.currentLocation?.NameOrUniqueName,["Menu"]=Game1.activeClickableMenu?.GetType().Name,
        ["Hat"]=Game1.player?.hat.Value?.QualifiedItemId,["Shirt"]=Game1.player?.shirtItem.Value?.QualifiedItemId,
        ["Hair"]=Game1.player?.hair.Value,["WardrobeApplied"]=wardrobeApplied,
        ["Pants"]=Game1.player?.pantsItem.Value?.QualifiedItemId,["Hidden"]=Game1.player?.hidden.Value,
        ["FarmhandOffered"]=menu?.client.availableFarmhands?.Count,["ClientMessage"]=menu?.client.connectionMessage,
        ["ClientTimedOut"]=menu?.client.timedOut
    };
    private void Write(string file,JObject value)
    {
        string path=At(file); File.WriteAllText(path+".tmp",value.ToString()); File.Move(path+".tmp",path,true);
    }
}
