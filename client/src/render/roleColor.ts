// Pure role/kind -> colour mapping. Enemies are always red regardless of
// their role field; player-controlled roles get tank/healer/damage colours.

export const COLOR_TANK = 0x3a7bd5;
export const COLOR_HEALER = 0x2ecc71;
export const COLOR_DAMAGE = 0xe67e22;
export const COLOR_ENEMY = 0xe74c3c;
export const COLOR_UNKNOWN = 0x999999;

export function colorForActor(kind: "player" | "enemy", role: string): number {
  if (kind === "enemy") return COLOR_ENEMY;
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
