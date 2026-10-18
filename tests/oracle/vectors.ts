/**
 * tests/oracle/vectors.ts -- input -> output vector fixtures for the FROZEN TypeScript integer
 * primitives, so the C# port (lane 1C: IntMath, Fixed, Atan, Prng; lane 3A: the bot PRNG) can be
 * proven equal function by function before any trace is compared.
 *
 * Files (see game/sim/ORACLE.md "Vector fixtures"):
 *   fixed.vectors.json   sim/core/fixed.ts: isqrt, length, scale, normaliseTo, yawBetween, angleDiff
 *   atan.vectors.json    sim/core/atan.ts: yawFromComponents
 *   prng.vectors.json    sim/core/prng.ts: seedState, nextInt (incl. raw 32-bit draws, n = 2^32)
 *   botprng.vectors.json bots/src/skill.ts: seedPrng, stepPrng (float output; JS-double hazard rows)
 *   intdiv.vectors.json  JS Math.floor / Math.trunc / Math.ceil / Math.round of n/d and n % d
 *
 * Inputs are hand-picked edge cases (zero, negatives, wrap, ties, octant boundaries, extremes)
 * followed by RANDOM_ROWS seeded rows drawn from sim/core/prng.ts itself, so the inputs are as
 * reproducible as the outputs. An output of the string "throw" means the TS function throws.
 *
 * Usage: `import { buildVectors } from "./vectors";` -> Record<filename, text>.
 */
import { angleDiff, isqrt, length, normaliseTo, scale, yawBetween } from "@sim/core/fixed";
import { yawFromComponents } from "@sim/core/atan";
import { nextInt, seedState, type PrngState } from "@sim/core/prng";
import { seedPrng, stepPrng, type PrngState as BotPrngState } from "@bots/src/skill";
import { canonicalJson } from "./format";
import { ORACLE_VERSION, SOURCE_SHA } from "./traces";

/** Seeded random rows per vector file (split evenly across that file's functions). */
export const RANDOM_ROWS = 10000;
const TWO32 = 0x100000000;

type Cell = number | string | boolean;
interface FnVectors {
  name: string;
  from: string;
  columns: string[];
  notes: string;
  rows: Cell[][];
}

/** Deterministic input generator over sim/core/prng.ts (the oracle's own inputs are replayable). */
class Gen {
  private s: PrngState;
  constructor(seed: number) {
    this.s = seedState(seed);
  }
  u32(): number {
    const [v, s] = nextInt(this.s, TWO32);
    this.s = s;
    return v;
  }
  /** Uniform integer in [lo, hi], span <= 2^32. */
  int(lo: number, hi: number): number {
    const [v, s] = nextInt(this.s, hi - lo + 1);
    this.s = s;
    return lo + v;
  }
  /** Signed integer with a random magnitude class up to 2^bits (wide dynamic range). */
  wide(bits: number): number {
    const b = this.int(0, bits);
    const mag = b <= 32 ? this.int(0, 2 ** b - 1) : this.int(0, 2 ** (b - 32) - 1) * TWO32 + this.u32();
    return this.int(0, 1) === 0 ? mag : -mag;
  }
}

function attempt(f: () => Cell[]): Cell[] {
  try {
    return f();
  } catch {
    return ["throw"];
  }
}

function render(file: string, functions: FnVectors[]): string {
  const head = canonicalJson({ kind: "keyrun-oracle-vectors", version: ORACLE_VERSION, file, source: { sha: SOURCE_SHA } });
  const fnTexts = functions.map((fn) => {
    const meta = canonicalJson({ name: fn.name, from: fn.from, columns: fn.columns, notes: fn.notes, count: fn.rows.length });
    const rows = fn.rows.map((r) => canonicalJson(r)).join(",\n");
    return `{"meta":${meta},"rows":[\n${rows}\n]}`;
  });
  return `{"header":${head},"functions":[\n${fnTexts.join(",\n")}\n]}\n`;
}

function fixedVectors(): string {
  const g = new Gen(0x0f1e3d);
  const per = RANDOM_ROWS / 5;

  const isqrtRows: Cell[][] = [];
  const isqrtEdges = [0, 1, 2, 3, 4, 8, 9, 15, 16, 17, 24, 25, 26, 99, 100, 101, 2 ** 31 - 1, 2 ** 31, 2 ** 32 - 1, 2 ** 32,
    46340 ** 2, 46340 ** 2 - 1, 46341 ** 2, 2 ** 40, 2 ** 40 - 1, (2 ** 20 + 1) ** 2, (2 ** 20 + 1) ** 2 - 1, 2 ** 50, 2 ** 50 - 1,
    -1, -4, 1.5, 0.5];
  for (const n of isqrtEdges) isqrtRows.push([n, ...attempt(() => [isqrt(n)])]);
  for (let i = 0; i < per; i += 1) {
    const n = Math.abs(g.wide(48));
    isqrtRows.push([n, isqrt(n)]);
  }

  const lengthRows: Cell[][] = [];
  const lengthEdges: [number, number, number][] = [[0, 0, 0], [3, 0, 4], [-3, 0, -4], [1, 1, 1], [-1, -1, -1], [1000000, 0, 1000000],
    [-46341, 0, 0], [400, 0, 400], [399, 0, 401]];
  for (const [x, y, z] of lengthEdges) lengthRows.push([x, y, z, length({ x, y, z })]);
  for (let i = 0; i < per / 2; i += 1) {
    const v = { x: g.int(-1000000, 1000000), y: g.int(-1000, 1000), z: g.int(-1000000, 1000000) };
    lengthRows.push([v.x, v.y, v.z, length(v)]);
  }

  const scaleRows: Cell[][] = [];
  const scaleEdges: [number, number, number, number][] = [[0, 0, 0, 10000], [1, -1, 0, 5000], [3, -3, 1, 5000], [5, -5, 7, 1000],
    [1, 2, 3, 0], [1, 2, 3, -10000], [1, -1, 0, -5000], [-15000, 15000, 1, 3333], [7, -7, 0, 15000], [100, -100, 0, -1]];
  for (const [x, y, z, bp] of scaleEdges) {
    const r = scale({ x, y, z }, bp);
    scaleRows.push([x, y, z, bp, r.x, r.y, r.z]);
  }
  for (let i = 0; i < per; i += 1) {
    const v = { x: g.int(-1000000, 1000000), y: g.int(-5000, 5000), z: g.int(-1000000, 1000000) };
    const bp = g.int(-20000, 30000);
    const r = scale(v, bp);
    scaleRows.push([v.x, v.y, v.z, bp, r.x, r.y, r.z]);
  }

  const normRows: Cell[][] = [];
  const normEdges: [number, number, number, number][] = [[0, 0, 0, 1000], [1, 0, 0, 1000], [-1, 0, 0, 1000], [1, 0, 1, 1000],
    [-1, 0, -1, 1000], [3, 0, 4, 5], [3, 0, 4, 0], [3, 0, 4, -10], [1, 0, 1, 1], [-2, 0, 1, 3], [999999, 0, -1, 180]];
  for (const [x, y, z, len] of normEdges) {
    const r = normaliseTo({ x, y, z }, len);
    normRows.push([x, y, z, len, r.x, r.y, r.z]);
  }
  for (let i = 0; i < per / 2; i += 1) {
    const v = { x: g.int(-100000, 100000), y: g.int(-100, 100), z: g.int(-100000, 100000) };
    const len = g.int(0, 100000);
    const r = normaliseTo(v, len);
    normRows.push([v.x, v.y, v.z, len, r.x, r.y, r.z]);
  }

  const yawRows: Cell[][] = [];
  const yawEdges: [number, number, number, number][] = [[0, 0, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1], [0, 0, -1, 0], [0, 0, 0, -1],
    [5, 5, 5, 5], [100, -100, -100, 100], [-7, 3, 2, 3]];
  for (const [fx, fz, tx, tz] of yawEdges) yawRows.push([fx, fz, tx, tz, yawBetween({ x: fx, y: 0, z: fz }, { x: tx, y: 0, z: tz })]);
  for (let i = 0; i < per; i += 1) {
    const f = { x: g.int(-20000, 20000), y: 0, z: g.int(-20000, 20000) };
    const t = { x: g.int(-20000, 20000), y: 0, z: g.int(-20000, 20000) };
    yawRows.push([f.x, f.z, t.x, t.z, yawBetween(f, t)]);
  }

  const diffRows: Cell[][] = [];
  const diffEdges: [number, number][] = [[0, 0], [0, 512], [512, 0], [0, 513], [0, -512], [0, 1024], [0, -1024], [1023, 0],
    [0, 1023], [-1, 1], [1, -1], [-3000, 3000], [5000, -5000], [256, 768], [768, 256], [-512, 512], [0, 511], [0, -511]];
  for (const [a, b] of diffEdges) diffRows.push([a, b, angleDiff(a, b)]);
  for (let i = 0; i < per; i += 1) {
    const a = g.int(-3000, 3000);
    const b = g.int(-3000, 3000);
    diffRows.push([a, b, angleDiff(a, b)]);
  }

  return render("fixed.vectors.json", [
    { name: "isqrt", from: "sim/core/fixed.ts isqrt", columns: ["n", "out"], notes: "floor(sqrt(n)); throws on negative or non-integer n", rows: isqrtRows },
    { name: "length", from: "sim/core/fixed.ts length", columns: ["x", "y", "z", "out"], notes: "isqrt(x*x+y*y+z*z)", rows: lengthRows },
    { name: "scale", from: "sim/core/fixed.ts scale", columns: ["x", "y", "z", "bp", "outX", "outY", "outZ"], notes: "roundDiv(c*bp, 10000) per component; round half away from zero", rows: scaleRows },
    { name: "normaliseTo", from: "sim/core/fixed.ts normaliseTo", columns: ["x", "y", "z", "len", "outX", "outY", "outZ"], notes: "roundDiv(c*len, length(v)); zero vector -> zero", rows: normRows },
    { name: "yawBetween", from: "sim/core/fixed.ts yawBetween", columns: ["fromX", "fromZ", "toX", "toZ", "out"], notes: "y ignored; coincident points -> 0", rows: yawRows },
    { name: "angleDiff", from: "sim/core/fixed.ts angleDiff", columns: ["a", "b", "out"], notes: "JS % (truncating) then +1024 %1024; result in (-512, 512]", rows: diffRows },
  ]);
}

function atanVectors(): string {
  const g = new Gen(0xa7a11);
  const rows: Cell[][] = [];
  const edges: [number, number][] = [[0, 0], [1, 0], [0, 1], [-1, 0], [0, -1], [1, 1], [-1, 1], [-1, -1], [1, -1],
    [256, 1], [256, 255], [256, 256], [255, 256], [1, 256], [-256, 1], [1, -256], [1000000, 1], [1, 1000000], [-1000000, -1],
    [2 ** 30, 2 ** 30 - 1], [-(2 ** 30), 1], [3, 7], [-3, 7], [-3, -7], [3, -7], [7, 3], [-7, 3], [-7, -3], [7, -3],
    [1000, -1], [1000, 1], [-1000, -1], [-1000, 1]];
  for (const [dx, dz] of edges) rows.push([dx, dz, yawFromComponents(dx, dz)]);
  for (let i = 0; i < RANDOM_ROWS; i += 1) {
    const small = i % 4 === 0;
    const dx = small ? g.int(-300, 300) : g.int(-1000000, 1000000);
    const dz = small ? g.int(-300, 300) : g.int(-1000000, 1000000);
    rows.push([dx, dz, yawFromComponents(dx, dz)]);
  }
  return render("atan.vectors.json", [
    { name: "yawFromComponents", from: "sim/core/atan.ts yawFromComponents", columns: ["dx", "dz", "out"], notes: "0 = +x, 256 = +z, result 0..1023; (0,0) -> 0", rows },
  ]);
}

function prngVectors(): string {
  const g = new Gen(0x9e3779b9);
  const seedRows: Cell[][] = [];
  const seedEdges = [0, 1, 2, 7, 11, 42424242, 24681357, -1, -2, 2 ** 31 - 1, 2 ** 31, 2 ** 32 - 1, 2 ** 32, 2 ** 32 + 5, 1.5, -1.5];
  for (const seed of seedEdges) seedRows.push([seed, ...seedState(seed)]);
  for (let i = 0; i < 1000; i += 1) {
    const seed = g.u32();
    seedRows.push([seed, ...seedState(seed)]);
  }

  const drawRows: Cell[][] = [];
  const ns = [1, 2, 3, 10, 1000, 2 ** 31, 2 ** 31 + 1, 3 * 2 ** 30, TWO32 - 1, TWO32];
  for (const seed of [0, 1, 7, 11, 42424242]) {
    let s = seedState(seed);
    for (const n of ns) {
      const [v, next] = nextInt(s, n);
      drawRows.push([...s, n, v, ...next]);
      s = next;
    }
  }
  const throwingNs: number[] = [0, -1, 1.5];
  for (const n of throwingNs) drawRows.push([1, 2, 3, 4, n, ...attempt(() => { const [v, s] = nextInt([1, 2, 3, 4], n); return [v, ...s]; })]);
  for (let i = 0; i < RANDOM_ROWS - 1000; i += 1) {
    const s: PrngState = [g.u32(), g.u32(), g.u32(), g.u32()];
    if (s[0] === 0 && s[1] === 0 && s[2] === 0 && s[3] === 0) s[0] = 1;
    const kind = i % 4;
    const n = kind === 0 ? TWO32 : kind === 1 ? g.int(1, 64) : kind === 2 ? g.int(1, 2 ** 31) : g.int(2 ** 31, TWO32 - 1);
    const [v, next] = nextInt(s, n);
    drawRows.push([...s, n, v, ...next]);
  }
  return render("prng.vectors.json", [
    { name: "seedState", from: "sim/core/prng.ts seedState", columns: ["seed", "s0", "s1", "s2", "s3"], notes: "splitmix32 over seed>>>0 (ToUint32: negatives wrap, fractions truncate); all-zero -> s0=1", rows: seedRows },
    { name: "nextInt", from: "sim/core/prng.ts nextInt", columns: ["s0", "s1", "s2", "s3", "n", "value", "n0", "n1", "n2", "n3"], notes: "xoshiro128** with rejection sampling below floor(2^32/n)*n; n = 2^32 yields the raw output; first block chains state across draws; throws for n<=0 or non-integer (n > 2^32 never returns and is not vectored)", rows: drawRows },
  ]);
}

function botPrngVectors(): string {
  const g = new Gen(0xb07);
  const seedRows: Cell[][] = [];
  // The largest seed whose s*2654435761 is still <= 2^53 (exact), and its neighbours.
  const exactMax = Math.floor(Number.MAX_SAFE_INTEGER / 2654435761);
  const seedEdges = [0, 1, 7, 11, -1, 2 ** 31, 2 ** 32 - 1, 2 ** 32, exactMax - 1, exactMax, exactMax + 1, exactMax + 2, 1000000, 2 ** 24];
  const above2p53 = (s: number): boolean => (s >>> 0 || 1) * 2654435761 > Number.MAX_SAFE_INTEGER;
  for (const seed of seedEdges) seedRows.push([seed, ...seedPrng(seed), above2p53(seed)]);
  for (let i = 0; i < 500; i += 1) {
    const seed = g.u32();
    seedRows.push([seed, ...seedPrng(seed), above2p53(seed)]);
  }
  const stepRows: Cell[][] = [];
  for (const seed of [1, 7, 11]) {
    let s: BotPrngState = seedPrng(seed);
    for (let k = 0; k < 16; k += 1) {
      const { value, next } = stepPrng(s);
      stepRows.push([...s, value, ...next]);
      s = next;
    }
  }
  for (let i = 0; i < RANDOM_ROWS - 500; i += 1) {
    const s: BotPrngState = [g.u32(), g.u32(), g.u32(), g.u32()];
    const { value, next } = stepPrng(s);
    stepRows.push([...s, value, ...next]);
  }
  return render("botprng.vectors.json", [
    { name: "seedPrng", from: "bots/src/skill.ts seedPrng", columns: ["seed", "s0", "s1", "s2", "s3", "productAbove2p53"], notes: "s = seed>>>0 || 1; s1 = (s*2654435761)>>>0 computed in JS DOUBLES: when s*2654435761 > 2^53 (productAbove2p53) the product MAY be rounded before >>>0 (e.g. seed -1 gives 1640531968, a uint32 multiply gives 1640531535), so reproduce the double product, not a uint32 multiply; s3 = (s*40503)>>>0 is always exact for uint32 s", rows: seedRows },
    { name: "stepPrng", from: "bots/src/skill.ts stepPrng", columns: ["s0", "s1", "s2", "s3", "value", "n0", "n1", "n2", "n3"], notes: "xorshift128; value = n3 / 4294967295 as an IEEE double (shortest round-trip JSON); value can be exactly 1 when n3 = 2^32-1", rows: stepRows },
  ]);
}

function intDivVectors(): string {
  const g = new Gen(0xd1d);
  const rows: Cell[][] = [];
  const edges: [number, number][] = [[7, 2], [-7, 2], [7, -2], [-7, -2], [0, 5], [0, -5], [5, 10], [-5, 10], [15, 10], [-15, 10],
    [25, 10], [-25, 10], [1, 3], [-1, 3], [2, 3], [-2, 3], [10000, 10000], [-10000, 10000], [-1, 1024], [1023, 1024], [-1025, 1024],
    [2 ** 40, 3], [-(2 ** 40), 3], [9, 4], [-9, 4], [11, 4], [-11, 4], [2, 4], [-2, 4], [6, 4], [-6, 4]];
  const row = (n: number, d: number): Cell[] => {
    const q = n / d;
    const rem = n % d;
    return [n, d, Math.floor(q), Math.trunc(q), Math.ceil(q), Math.round(q), Object.is(rem, -0) ? 0 : rem];
  };
  for (const [n, d] of edges) rows.push(row(n, d));
  for (let i = 0; i < RANDOM_ROWS; i += 1) {
    const n = i % 2 === 0 ? g.int(-100000, 100000) : g.wide(40);
    let d = i % 3 === 0 ? g.int(-16, 16) : g.int(-1000000, 1000000);
    if (d === 0) d = 1;
    rows.push(row(n, d));
  }
  return render("intdiv.vectors.json", [
    { name: "jsIntDiv", from: "JS semantics used across sim/ (see ORACLE.md hazard table)", columns: ["n", "d", "floor", "trunc", "ceil", "round", "rem"], notes: "floor/trunc/ceil/round of the double n/d (exact for |n| < 2^53); round = Math.round (ties toward +infinity); rem = JS n % d (sign of n, -0 rendered 0)", rows },
  ]);
}

/** All vector fixture files, filename -> text. */
export function buildVectors(): Record<string, string> {
  return {
    "fixed.vectors.json": fixedVectors(),
    "atan.vectors.json": atanVectors(),
    "prng.vectors.json": prngVectors(),
    "botprng.vectors.json": botPrngVectors(),
    "intdiv.vectors.json": intDivVectors(),
  };
}
