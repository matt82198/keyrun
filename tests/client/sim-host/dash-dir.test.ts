/**
 * tests/client/sim-host/dash-dir.test.ts
 *
 * client/src/sim-host/player-commands.ts's resolveDashDir (the one dash-direction authority shared by
 * the local host and net/mapping.ts) must use the sim's own yaw convention for a no-move dash:
 * sim/core/atan.ts's yawFromComponents and sim/combat/targeting.ts's faceDir both treat yaw 0 as +x and
 * yaw 256 as +z. A sin/cos swap here mirrors every facing dash across the x=z diagonal.
 */
import { describe, expect, it } from "vitest";
import { resolveDashDir } from "../../../client/src/sim-host/player-commands.js";
import { yawFromComponents } from "../../../sim/core/atan.js";
import { faceDir } from "../../../sim/combat/targeting.js";
import { validateCommand } from "../../../sim/core/commands.js";

describe("resolveDashDir: no-move dash follows the sim's facing convention", () => {
  it("yaw 0 dashes toward +x, yaw 256 toward +z, 512 toward -x, 768 toward -z", () => {
    expect(resolveDashDir(undefined, 0)).toEqual({ x: 1000, z: 0 });
    expect(resolveDashDir(undefined, 256)).toEqual({ x: 0, z: 1000 });
    expect(resolveDashDir({ dx: 0, dz: 0 }, 512)).toEqual({ x: -1000, z: 0 });
    expect(resolveDashDir(undefined, 768)).toEqual({ x: 0, z: -1000 });
  });

  it("round-trips through yawFromComponents at 16 yaws, matching faceDir's direction", () => {
    for (let i = 0; i < 16; i++) {
      const yaw = i * 64;
      const dir = resolveDashDir(undefined, yaw);
      expect(yawFromComponents(dir.x, dir.z)).toBe(yaw);
      const face = faceDir(yaw);
      expect(Math.sign(dir.x)).toBe(Math.sign(face.x));
      expect(Math.sign(dir.z)).toBe(Math.sign(face.z));
      const cmd = { tick: 1, actorId: 1, kind: "dash" as const, dir, yaw: yawFromComponents(dir.x, dir.z) };
      expect(validateCommand(cmd)).toEqual(cmd);
    }
  });

  it("a non-zero move this tick still wins over facing", () => {
    expect(resolveDashDir({ dx: 0, dz: -180 }, 0)).toEqual({ x: 0, z: -180 });
  });
});
