/**
 * tests/content/registries.test.ts
 * Usage: `npm test` (vitest). Confirms content/index.ts loads without throwing and that every
 * registry has the expected shape and counts from BIBLE.md Sec 5/Sec 8/Sec 9.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "../../content/index";

describe("content registries", () => {
  it("loads without throwing", () => {
    expect(() => loadContent()).not.toThrow();
  });

  it("has 3 classes, 38 abilities, 22 effects, 9 combos, 7 enemies, 1 dungeon, 4 clauses", () => {
    const reg = loadContent();
    expect(reg.classes.size).toBe(3);
    expect(reg.abilities.size).toBe(38);
    expect(reg.effects.size).toBe(22);
    expect(reg.combos.size).toBe(9);
    expect(reg.enemies.size).toBe(7);
    expect(reg.dungeons.size).toBe(1);
    expect(reg.clauses.size).toBe(4);
  });

  it("has the three named starter classes", () => {
    const reg = loadContent();
    expect([...reg.classes.keys()].sort()).toEqual(["chanter", "cinder", "warden"]);
  });

  it("has the Hollow Vault dungeon with 3 pulls and a boss", () => {
    const reg = loadContent();
    const dungeon = reg.dungeons.get("hollow-vault");
    expect(dungeon?.pulls.length).toBe(3);
    expect(dungeon?.boss.enemyId).toBe("hollowWarden.boss");
  });

  it("every class has 12-15 talent options across its 5 forks", () => {
    const reg = loadContent();
    for (const cls of reg.classes.values()) {
      expect(cls.talents.length).toBe(5);
      const total = cls.talents.reduce((n, fork) => n + fork.options.length, 0);
      expect(total).toBeGreaterThanOrEqual(12);
      expect(total).toBeLessThanOrEqual(15);
    }
  });
});
