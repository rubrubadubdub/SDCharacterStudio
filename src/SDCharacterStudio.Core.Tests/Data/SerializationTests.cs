using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;

public class SerializationTests
{
    [Fact]
    public void CommentsAndPolymorphicBeatsAndTranslatedTextRoundTrip()
    {
        var scene = PackJson.Deserialize<Cutscene>("""
            { /* human note */ "Format":"1.0", "Id":"talk", "Location":"ScienceHouse",
              "Beats":[{"Type":"Say","Actor":"Robin","Text":{"i18n":"greeting"},"Emotion":2},
                       {"Type":"Move","Actor":"Robin","Speed":3,"To":{"Tile":[2,4]}}] }
            """);
        Assert.IsType<SayBeat>(scene!.Beats[0]);
        Assert.IsType<MoveBeat>(scene.Beats[1]);
        var json = JObject.Parse(PackJson.Serialize(scene));
        Assert.Equal("greeting", (string?)json["Beats"]![0]!["Text"]!["i18n"]);
        Assert.Equal(2, (int)json["Beats"]![0]!["Emotion"]!);
        Assert.Equal(3, (int)json["Beats"]![1]!["Speed"]!);
    }

    [Fact]
    public void UnknownLayerAndBeatRetainTheirPayloadForLocatedValidation()
    {
        var scene = PackJson.Deserialize<Cutscene>("""{"Format":"1.0","Id":"talk","Location":"Town","Beats":[{"Type":"Future","Extra":42}]}""");
        Assert.IsType<UnknownBeat>(scene!.Beats[0]);
        Assert.Equal(42, (int)JObject.Parse(PackJson.Serialize(scene))["Beats"]![0]!["Extra"]!);
        var pose = PackJson.Deserialize<Pose>("""{"Frames":[{"Layers":[{"Type":"FutureLayer","Extra":true}]}]}""");
        Assert.IsType<UnknownLayer>(pose!.Frames[0].Layers[0]);
    }

    [Fact]
    public void OmittedPivotSerializesToFeetOfCurrentFrameSizeAndExplicitPivotIsPreserved()
    {
        var pose=PackJson.Deserialize<Pose>("""{"Format":"1.0","Id":"wide","FrameSize":[32,24],"Frames":[]}""")!;
        Assert.Equal(new[]{16,23},Assert.IsType<JArray>(JObject.Parse(PackJson.Serialize(pose))["Pivot"]).Values<int>());
        pose.FrameSize=new[]{48,32};
        Assert.Equal(new[]{24,31},JObject.Parse(PackJson.Serialize(pose))["Pivot"]!.Values<int>());
        pose.Pivot=new[]{7,20};
        Assert.Equal(new[]{7,20},JObject.Parse(PackJson.Serialize(pose))["Pivot"]!.Values<int>());
    }

}
