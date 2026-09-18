/**
 * client/src/sim-host/content-abilities.ts
 *
 * Converts content's ability/effect data shapes (content/schema/ability.ts, effect.ts -- copied
 * locally as content-registry.ts's C*/ types, see that file's docstring for why) into the real,
 * merged sim/combat/abilities.ts + effects.ts shapes. content/CLAUDE.md's "KNOWN DRIFT" section
 * documents this gap; PR #15 has since landed sim/combat/windows.ts (window/payoff ops DO exist in
 * the real sim now), narrowing but not closing it. Differences this file bridges:
 *  - content's "combo" op (`{op:"combo",comboTable}`) -> sim's tag-free "payoff" op.
 *  - content's "window" op has no `setupClass`; sim's requires one -- `ownerClass` (the player
 *    class, or an enemy pseudo-class, that owns the ability carrying the op) is threaded in.
 *  - content's "interrupt" op carries `lockoutTicks`; sim's takes none (RunContent.lockoutTicks is
 *    the single global lockout duration instead) -- dropped.
 *  - content's "resource" op carries a `resource` name; sim's has only `delta` (one resource per
 *    actor) -- dropped.
 *  - content's line shape carries `width` (half-width); sim's line shape has no `width` field, only
 *    `radius`, which IS the half-width for a line per content/CLAUDE.md's own documented note.
 *  - content's `cost` is optional; sim's is required -- defaults to a free (0-cost, "none"
 *    resource) ability when absent (matches every case seen in content/data: `cost` is always
 *    supplied when an ability actually spends one).
 *  - content's `applyEffect.stacks` is optional; sim's is required -- defaults to 1.
 *  - Chanter's stance-differentiated abilities (`variants: {mend, ward}`) collapse to the "mend"
 *    variant always -- sim's AbilityDef has no variants field at all (no stance state machine).
 *    DEVIATION: this means Chanter always plays as if permanently in Mend (heals), Refrain's own
 *    stance-swap burst and every Ward-only behaviour never fire. Documented, not silently dropped.
 *  - An ability whose effects list becomes empty after stripping (a pure Setup ability that only
 *    carried a "window"/"combo" op, or content's literal `effects: []` on Blink) gets a single
 *    harmless `{op:"resource",delta:0}` placeholder -- sim's cast pipeline does not require
 *    non-empty effects at runtime, but this keeps every converted ability visibly non-degenerate.
 *
 * Usage: `const simAbility = toSimAbility(contentAbility, "warden");`
 */
import type { AbilityDef as SimAbilityDef, AbilityShape, EffectOp as SimEffectOp } from "../../../sim/combat/abilities.js";
import type { EffectDef as SimEffectDef } from "../../../sim/combat/effects.js";
import type { CAbilityDef, CEffectDef, CEffectOp, CShapeDef } from "./content-registry.js";

export function toSimEffectOp(op: CEffectOp, ownerClass: string): SimEffectOp {
  switch (op.op) {
    case "damage":
      return { op: "damage", amount: op.amount, school: op.school };
    case "heal":
      return { op: "heal", amount: op.amount };
    case "applyEffect":
      return { op: "applyEffect", effectId: op.effectId, durationTicks: op.durationTicks, stacks: op.stacks ?? 1 };
    case "removeEffect":
      return { op: "removeEffect", effectId: op.effectId };
    case "interrupt":
      return { op: "interrupt" };
    case "resource":
      return { op: "resource", delta: op.delta };
    case "threat":
      return {
        op: "threat",
        ...(op.amount !== undefined ? { amount: op.amount } : {}),
        ...(op.multiplierBp !== undefined ? { multiplierBp: op.multiplierBp } : {}),
      };
    case "window":
      return { op: "window", windowId: op.windowId, durationTicks: op.durationTicks, onTarget: op.onTarget, setupClass: ownerClass };
    case "combo":
      return { op: "payoff" };
  }
}

function toSimShape(shape: CShapeDef): AbilityShape {
  if (shape.kind === "line") {
    return {
      kind: "line",
      ...(shape.length !== undefined ? { length: shape.length } : {}),
      // content's line half-width lives in `width`; sim's AbilityShape has no `width` field, only
      // `radius` (which it treats as the half-width for a line -- see file docstring).
      ...(shape.width !== undefined ? { radius: shape.width } : shape.radius !== undefined ? { radius: shape.radius } : {}),
    };
  }
  return {
    kind: shape.kind,
    ...(shape.radius !== undefined ? { radius: shape.radius } : {}),
    ...(shape.angle1024 !== undefined ? { angle1024: shape.angle1024 } : {}),
  };
}

const NOOP_EFFECT: SimEffectOp = { op: "resource", delta: 0 };

export function toSimAbility(a: CAbilityDef, ownerClass: string): SimAbilityDef {
  const source = a.variants?.mend ?? a.effects;
  const effects = source.map((op) => toSimEffectOp(op, ownerClass));
  return {
    id: a.id,
    castTicks: a.castTicks,
    gcdTicks: a.gcdTicks,
    cooldownTicks: a.cooldownTicks,
    charges: a.charges,
    cost: a.cost ?? { resource: "none", amount: 0 },
    range: a.range,
    shape: toSimShape(a.shape),
    interruptible: a.interruptible,
    effects: effects.length > 0 ? effects : [NOOP_EFFECT],
  };
}

export function toSimEffectDef(e: CEffectDef): SimEffectDef {
  return {
    id: e.id,
    maxStacks: e.maxStacks,
    dispellableTag: e.dispellable ?? null,
    modifiers: {
      ...(e.modifiers?.damageDoneBp !== undefined ? { damageDoneBp: e.modifiers.damageDoneBp } : {}),
      ...(e.modifiers?.damageTakenBp !== undefined ? { damageTakenBp: e.modifiers.damageTakenBp } : {}),
      ...(e.modifiers?.hasteBp !== undefined ? { hasteBp: e.modifiers.hasteBp } : {}),
      ...(e.modifiers?.movementBp !== undefined ? { movementBp: e.modifiers.movementBp } : {}),
    },
    // Periodic ops in this content are always damage/heal (never window/combo), so the owner class
    // passed here is never actually read by toSimEffectOp; "none" documents that rather than
    // threading a real class id through EffectDef conversion for no reason.
    periodic: e.periodic ? { everyTicks: e.periodic.everyTicks, op: toSimEffectOp(e.periodic.op, "none") } : null,
  };
}
