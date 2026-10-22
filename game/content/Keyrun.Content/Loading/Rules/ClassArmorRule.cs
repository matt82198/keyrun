using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class ClassArmorRule : IContentRule
{
    // design/BIBLE.md section 3.1: armor is a passive class trait, capped at 90% so a hit always lands.
    public void Check(ContentRegistry reg)
    {
        foreach (var (classId, armorBp) in reg.Run.ClassArmorBp)
        {
            if (!reg.Classes.ContainsKey(classId)) Fail("run classArmorBp has unknown class \"" + classId + "\"");
            if (armorBp < 0 || armorBp > 9000) Fail("run classArmorBp." + classId + " must be in [0, 9000], got " + armorBp);
        }
    }
}
