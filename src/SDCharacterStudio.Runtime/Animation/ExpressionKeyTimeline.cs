using SDCharacterStudio.Core.Models;
namespace SDCharacterStudio.Runtime.Animation;
public static class ExpressionKeyTimeline
{
    public static ExpressionKey? Select(IReadOnlyList<ExpressionKey> keys,double time)
    {
        for(int i=keys.Count-1;i>=0;i--)
        {
            var key=keys[i];if(key.At>time)continue;
            if(key.Expression==null)return null;
            if(key.Duration.HasValue&&time-key.At>=key.Duration.Value)continue;
            return key;
        }
        return null;
    }
}
