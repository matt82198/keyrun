// Pure role/kind -> colour mapping. Enemies are red regardless of their role field, except the
// dungeon boss (D10 readability): its classId is the boss enemy def id, which by content convention
// ends in ".boss" (content-dungeon.ts's boss pull spawns classId = bossDef.id, "hollowWarden.boss").
// Player-controlled roles get tank/healer/damage colours.

export const COLOR_TANK = 0x3a7bd5;
export const COLOR_HEALER = 0x2ecc71;
export const COLOR_DAMAGE = 0xe67e22;
export const COLOR_ENEMY = 0xe74c3c;
export const COLOR_BOSS = 0x9b59b6;
export const COLOR_UNKNOWN = 0x999999;

const BOSS_CLASS_SUFFIX = ".boss";

export function colorForActor(kind: "player" | "enemy", role: string, classId?: string): number {
  if (kind === "enemy") return classId?.endsWith(BOSS_CLASS_SUFFIX) ? COLOR_BOSS : COLOR_ENEMY;
  switch (role) {
    case "tank":
      return COLOR_TANK;
    case "healer":
      return COLOR_HEALER;
    case "damage":
      return COLOR_DAMAGE;
    default:
      return COLOR_UNKNOWN;
  }
}
