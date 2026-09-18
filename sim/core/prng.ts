// Usage: xoshiro128** PRNG over an explicit 4x uint32 state tuple. Pure and immutable: every
// draw takes a state and returns [value, newState] rather than mutating anything in place, so
// the same seed always replays the same sequence regardless of call order elsewhere in the sim.

export type PrngState = [number, number, number, number];

function rotl(x: number, k: number): number {
  return ((x << k) | (x >>> (32 - k))) >>> 0;
}

/** splitmix32: expands a single integer seed into well-mixed uint32 values for seeding xoshiro. */
function splitmix32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x9e3779b9) >>> 0;
    let z = a;
    z = Math.imul(z ^ (z >>> 16), 0x21f0aaad) >>> 0;
    z = Math.imul(z ^ (z >>> 15), 0x735a2d97) >>> 0;
    z = (z ^ (z >>> 15)) >>> 0;
    return z;
  };
}

/** Derive a xoshiro128** state from a single numeric seed. Never returns the all-zero state. */
export function seedState(seed: number): PrngState {
  const gen = splitmix32(seed);
  const s: PrngState = [gen(), gen(), gen(), gen()];
  if (s[0] === 0 && s[1] === 0 && s[2] === 0 && s[3] === 0) s[0] = 1;
  return s;
}

/** One xoshiro128** step: returns the next raw uint32 output and the advanced state. */
function next(state: PrngState): [number, PrngState] {
  let [s0, s1, s2, s3] = state;
  const result = (rotl((s1 * 5) >>> 0, 7) * 9) >>> 0;
  const t = (s1 << 9) >>> 0;
  s2 = (s2 ^ s0) >>> 0;
  s3 = (s3 ^ s1) >>> 0;
  s1 = (s1 ^ s2) >>> 0;
  s0 = (s0 ^ s3) >>> 0;
  s2 = (s2 ^ t) >>> 0;
  s3 = rotl(s3, 11);
  return [result, [s0, s1, s2, s3]];
}

/** Draw an unbiased integer in [0, n) via rejection sampling on the raw 32-bit output. */
export function nextInt(state: PrngState, n: number): [number, PrngState] {
  if (!Number.isInteger(n) || n <= 0) throw new Error("nextInt: n must be a positive integer");
  const limit = Math.floor(0x100000000 / n) * n;
  let s = state;
  for (;;) {
    const [raw, advanced] = next(s);
    s = advanced;
    if (raw < limit) return [raw % n, s];
  }
}
