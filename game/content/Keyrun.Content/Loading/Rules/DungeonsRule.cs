using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class DungeonsRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        foreach (DungeonDef dungeon in reg.Dungeons.Values)
        {
            foreach (PullDef pull in dungeon.Pulls)
                foreach (PullEnemy enemy in pull.Enemies)
                    if (!reg.Enemies.ContainsKey(enemy.EnemyId))
                        Fail("dungeon \"" + dungeon.Id + "\" pull \"" + pull.Id + "\" references unknown enemy \"" + enemy.EnemyId + "\"");
            if (!reg.Enemies.ContainsKey(dungeon.Boss.EnemyId))
                Fail("dungeon \"" + dungeon.Id + "\" boss references unknown enemy \"" + dungeon.Boss.EnemyId + "\"");
        }
    }
}
