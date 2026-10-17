/**
 * client/src/render/ground-frame.ts
 *
 * One authority for laying a flat decal on the ground and pointing it, shared by render/telegraphs.ts
 * and render/cast-fx.ts. Decal geometry is authored in its local XY plane with FORWARD = local +x
 * (cone arcs centred on +x, lines running x in [0, length], half-width along y). The mesh is laid flat
 * with rotation.x = -PI/2 and turned with rotation.y under Euler order "YXZ" (flatten first, then yaw
 * about the world up axis). Under the default "XYZ" order the yaw would be applied BEFORE the flatten
 * and tilt the decal out of the ground plane -- the pre-D10 telegraph bug that stood line decals up
 * as walls and tipped rotated cones.
 *
 * The sim has ONE facing convention (sim/combat/targeting.ts faceDir): yaw 0 = +x, 256 = +z. Combat
 * casts and encounter telegraphs (sim/encounter/telegraphs.ts pointInCone/pointInLine, which read
 * faceDir and receive the caster's combat yaw verbatim) both resolve on it, so both decal kinds use
 * the same rotation; telegraphYawToThree is kept as a named alias for the telegraph call sites.
 *
 * Usage: `mesh.rotation.order = "YXZ"; mesh.rotation.x = -Math.PI / 2; mesh.rotation.y = combatYawToThree(yaw);`
 */
const YAW_UNITS = 1024;

/** rotation.y that points local +x along sim/combat's faceDir(yaw) = (cos, sin) in (x, z). */
export function combatYawToThree(yaw: number): number {
  return -(yaw / YAW_UNITS) * Math.PI * 2;
}

/** rotation.y for an encounter telegraph: same faceDir convention as combat, so the same rotation. */
export function telegraphYawToThree(yaw: number): number {
  return combatYawToThree(yaw);
}

/** Where a decal-local (x, y) point lands on the ground (x, z) for a mesh at `origin` turned by
 * `rotationY` -- the same transform three.js applies under order "YXZ" with rotation.x = -PI/2. */
export function decalPointToGround(localX: number, localY: number, rotationY: number, origin: { x: number; z: number }): { x: number; z: number } {
  const fx = localX;
  const fz = -localY; // flatten: local (x, y, 0) -> (x, 0, -y)
  const c = Math.cos(rotationY);
  const s = Math.sin(rotationY);
  return { x: origin.x + fx * c + fz * s, z: origin.z - fx * s + fz * c };
}
