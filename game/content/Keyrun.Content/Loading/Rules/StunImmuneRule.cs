using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class StunImmuneRule : IContentRule
{
    /// <summary>run.json stunImmuneEnemies (finding f-c63b43bf): every id names a known enemy.</summary>
    public void Check(ContentRegistry reg)
    {
        foreach (string id in reg.Run.StunImmuneEnemies)
        {
            if (!reg.Enemies.ContainsKey(id))
                Fail("run stunImmuneEnemies \"" + id + "\" is not an enemy");
        }
    }
}
