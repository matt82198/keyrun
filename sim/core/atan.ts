// Usage: integer-only atan2-style yaw lookup used by sim/core/fixed.ts's yawBetween.
// yawFromComponents(dx, dz) returns the yaw (0..1023, matching fixed.ts's YAW_MAX) of the vector
// (dx, dz) via octant reduction onto a fixed-point atan(i/256) lookup table (257 entries, i=0..256)
// with integer linear interpolation.
//
// ATAN_TABLE is static data: it was generated once, offline, via Math.atan and pasted below as integer
// literals. This file never calls Math.atan, Math.atan2, Math.sin, Math.cos, or Math.sqrt at runtime, so
// results are bit-identical on every engine -- the exact problem that made the old Math.atan2-based
// yawBetween unsafe on the sim path (a float that rounds to a different bucket at boundaries).
//
// Table values are atan(i/256) in yaw units (1024 = full turn), scaled by SUBSCALE for interpolation
// precision; ATAN_TABLE[256] === 128 * SUBSCALE exactly (atan(1) === 45 degrees === yaw 128).

const SUBSCALE = 4096;
const QUARTER = 256; // yaw units per quadrant (YAW_MAX / 4)
const YAW_MAX = 1024;

// atan(i/256) for i = 0..256, in yaw units * SUBSCALE. Regenerate with:
//   node -e "for(let i=0;i<=256;i++) console.log(Math.round(Math.atan(i/256)/(2*Math.PI)*1024*4096))"
const ATAN_TABLE: readonly number[] = [
  0, 2608, 5215, 7822, 10430, 13036, 15643, 18249, 20854, 23459, 26063, 28666, 31268, 33870, 36470, 39069,
  41667, 44264, 46860, 49454, 52046, 54637, 57226, 59814, 62400, 64984, 67566, 70146, 72724, 75299, 77873, 80444,
  83012, 85579, 88142, 90703, 93262, 95817, 98370, 100920, 103467, 106011, 108552, 111090, 113624, 116155, 118683, 121207,
  123728, 126245, 128759, 131269, 133775, 136277, 138776, 141270, 143761, 146247, 148730, 151208, 153682, 156151, 158617, 161078,
  163534, 165986, 168433, 170876, 173314, 175748, 178176, 180600, 183019, 185433, 187842, 190246, 192645, 195039, 197427, 199811,
  202189, 204562, 206930, 209292, 211649, 214000, 216346, 218686, 221021, 223350, 225674, 227992, 230304, 232611, 234911, 237206,
  239495, 241778, 244056, 246327, 248592, 250852, 253105, 255352, 257594, 259829, 262058, 264281, 266498, 268708, 270912, 273110,
  275302, 277488, 279667, 281840, 284006, 286167, 288321, 290468, 292609, 294744, 296872, 298994, 301109, 303218, 305320, 307416,
  309505, 311588, 313664, 315734, 317797, 319854, 321904, 323948, 325985, 328015, 330039, 332056, 334066, 336070, 338068, 340058,
  342043, 344020, 345991, 347955, 349913, 351864, 353809, 355746, 357678, 359602, 361520, 363432, 365337, 367235, 369126, 371011,
  372890, 374762, 376627, 378486, 380338, 382183, 384022, 385855, 387681, 389500, 391313, 393119, 394919, 396712, 398499, 400280,
  402053, 403821, 405582, 407336, 409085, 410826, 412562, 414291, 416013, 417729, 419439, 421143, 422840, 424531, 426215, 427893,
  429565, 431231, 432891, 434544, 436191, 437832, 439466, 441095, 442717, 444334, 445944, 447548, 449145, 450737, 452323, 453903,
  455476, 457044, 458606, 460162, 461711, 463255, 464793, 466325, 467851, 469372, 470886, 472395, 473897, 475394, 476886, 478371,
  479851, 481325, 482793, 484256, 485713, 487164, 488610, 490050, 491484, 492913, 494337, 495755, 497167, 498574, 499975, 501371,
  502762, 504147, 505527, 506901, 508270, 509634, 510992, 512345, 513693, 515036, 516373, 517705, 519032, 520354, 521670, 522982,
  524288,
];

function tableAt(i: number): number {
  const v = ATAN_TABLE[i];
  if (v === undefined) throw new Error(`atan.ts: table index ${i} out of range`);
  return v;
}

/** Rounds a non-negative n/d to the nearest integer, ties away from zero. Requires d > 0. */
function roundDivNonNeg(n: number, d: number): number {
  return Math.floor((n + Math.floor(d / 2)) / d);
}

/**
 * atan(numerator/denominator) in yaw units * SUBSCALE, via table lookup + integer linear interpolation.
 * Requires 0 <= numerator <= denominator and denominator > 0.
 */
function atanLookupScaled(numerator: number, denominator: number): number {
  const scaledIndex = numerator * 256;
  const idx = Math.floor(scaledIndex / denominator);
  if (idx >= 256) return tableAt(256);
  const rem = scaledIndex - idx * denominator;
  const lo = tableAt(idx);
  if (rem === 0) return lo;
  const hi = tableAt(idx + 1);
  return lo + roundDivNonNeg((hi - lo) * rem, denominator);
}

/**
 * Integer-only yaw (0..YAW_MAX-1) of the direction (dx, dz), matching the standard atan2(dz, dx)
 * convention mapped onto [0, YAW_MAX) -- 0 = +x, YAW_MAX/4 = +z. Octant-reduces (dx, dz) onto the
 * first octant [0, 45deg], looks up the fixed-point atan approximation, then mirrors back out to
 * the true octant.
 * Zero vector (dx === 0 && dz === 0) returns 0 (documented, arbitrary but deterministic).
 */
export function yawFromComponents(dx: number, dz: number): number {
  if (dx === 0 && dz === 0) return 0;

  const ax = Math.abs(dx);
  const az = Math.abs(dz);
  const swap = az > ax;
  const unsignedScaled = swap
    ? QUARTER * SUBSCALE - atanLookupScaled(ax, az)
    : atanLookupScaled(az, ax);

  let resultScaled: number;
  if (dx >= 0 && dz >= 0) resultScaled = unsignedScaled;
  else if (dx < 0 && dz >= 0) resultScaled = 2 * QUARTER * SUBSCALE - unsignedScaled;
  else if (dx < 0 && dz < 0) resultScaled = 2 * QUARTER * SUBSCALE + unsignedScaled;
  else resultScaled = 4 * QUARTER * SUBSCALE - unsignedScaled;

  return roundDivNonNeg(resultScaled, SUBSCALE) % YAW_MAX;
}
