// One actor's scene-graph view for render/actors.ts's ActorPool (split out to keep actors.ts under
// the 190-LOC gate). Group child order is load-bearing for tests: [0] capsule, [1] health bar,
// [2] resource bar, [3] cast bar, [4] name tag (render/name-tag.ts), [5] facing nose (points along
// local +x, the sim's yaw-0 facing), [6] white ground ring shown only under YOU (grey under a
// Downed party member, item 12), [7] status chips (render/status-chips.ts, item 5) above the health bar.
import * as THREE from "three";
import { createCanvasSprite, type CanvasSprite } from "./canvasSprite.js";
import { NAME_TAG_PX } from "./name-tag.js";

export const CAPSULE_RADIUS_M = 0.4;
export const CAPSULE_LENGTH_M = 1.0; // cylindrical section; total height ~= length + 2*radius
const BODY_TOP_M = CAPSULE_LENGTH_M + CAPSULE_RADIUS_M * 2;

export function drawBar(bar: CanvasSprite, fraction: number, fillColor: string): void {
  const { ctx, canvas } = bar;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = "rgba(10,10,14,0.75)";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  const clamped = Math.min(1, Math.max(0, fraction));
  ctx.fillStyle = fillColor;
  ctx.fillRect(1, 1, Math.max(0, clamped * (canvas.width - 2)), canvas.height - 2);
  bar.sync();
}

export interface ActorView {
  group: THREE.Group;
  capsule: THREE.Mesh;
  material: THREE.MeshStandardMaterial;
  healthBar: CanvasSprite;
  resourceBar: CanvasSprite;
  castBar: CanvasSprite;
  nameTag: CanvasSprite;
  nose: THREE.Mesh;
  selfRing: THREE.Mesh;
  chips: CanvasSprite;
}

const CHIP_PX: [number, number] = [512, 56];
const NAME_TAG_H = 0.064;
const CHIP_W = 0.29;
const CHIP_H = (CHIP_W * CHIP_PX[1]) / CHIP_PX[0];
const chipKeys = new WeakMap<CanvasSprite, string>();

/** Item 5: draws status chips (text + colour) left to right, centred; hidden when there are none. */
export function drawChips(sprite: CanvasSprite, chips: readonly { text: string; color: string }[]): void {
  sprite.sprite.visible = chips.length > 0;
  const key = chips.map((c) => `${c.text}|${c.color}`).join(",");
  if (chips.length === 0 || chipKeys.get(sprite) === key) return;
  chipKeys.set(sprite, key);
  const { ctx, canvas } = sprite;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.font = "bold 30px sans-serif";
  ctx.textBaseline = "middle";
  ctx.textAlign = "center";
  const widths = chips.map((c) => (ctx.measureText?.(c.text).width ?? c.text.length * 17) + 18);
  let x = Math.max(0, (canvas.width - widths.reduce((a, b) => a + b + 6, -6)) / 2);
  chips.forEach((c, i) => {
    const w = widths[i]!;
    ctx.fillStyle = "rgba(10,10,14,0.85)";
    ctx.fillRect(x, 4, w, canvas.height - 8);
    ctx.fillStyle = c.color;
    ctx.fillRect(x, 4, 5, canvas.height - 8);
    ctx.fillText(c.text, x + w / 2 + 2, canvas.height / 2);
    x += w + 6;
  });
  sprite.sync();
}

export function buildActorView(): ActorView {
  const group = new THREE.Group();

  const geometry = new THREE.CapsuleGeometry(CAPSULE_RADIUS_M, CAPSULE_LENGTH_M, 4, 8);
  const material = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.6 });
  const capsule = new THREE.Mesh(geometry, material);
  capsule.position.y = CAPSULE_LENGTH_M / 2 + CAPSULE_RADIUS_M;
  group.add(capsule);

  const healthBar = createCanvasSprite(128, 16, 1.2, 0.14);
  group.add(healthBar.sprite);
  const resourceBar = createCanvasSprite(128, 16, 1.2, 0.08);
  group.add(resourceBar.sprite);
  const castBar = createCanvasSprite(128, 16, 1.4, 0.16);
  castBar.sprite.visible = false;
  group.add(castBar.sprite);
  // Constant screen size (~40 px tall at 720p) so a bot next to the camera does not fill the screen.
  const nameTag = createCanvasSprite(NAME_TAG_PX[0], NAME_TAG_PX[1], 0.29, NAME_TAG_H, false);
  group.add(nameTag.sprite);

  const nose = new THREE.Mesh(new THREE.ConeGeometry(0.16, 0.5, 10), new THREE.MeshBasicMaterial({ color: 0xffffff }));
  nose.rotation.z = -Math.PI / 2; // cone apex (+y) turned to +x
  nose.position.set(CAPSULE_RADIUS_M + 0.2, BODY_TOP_M * 0.62, 0);
  group.add(nose);

  const selfRing = new THREE.Mesh(
    new THREE.RingGeometry(0.62, 0.78, 40),
    new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.85, side: THREE.DoubleSide, depthWrite: false }),
  );
  selfRing.rotation.x = -Math.PI / 2;
  selfRing.position.y = 0.025;
  selfRing.visible = false;
  group.add(selfRing);

  // Constant screen size like the name tag, anchored by its top edge just under the tag (same world
  // point), so chips sit between the tag and the health bar at any zoom without overlapping the tag.
  const chips = createCanvasSprite(CHIP_PX[0], CHIP_PX[1], CHIP_W, CHIP_H, false);
  chips.sprite.center?.set(0.5, 1 + NAME_TAG_H / 2 / CHIP_H); // ?. : minimal test fakes of three omit it
  chips.sprite.visible = false;
  group.add(chips.sprite);

  return { group, capsule, material, healthBar, resourceBar, castBar, nameTag, nose, selfRing, chips };
}

/** Stacks the overhead sprites above a body scaled by `scaleY`: cast bar, resource, health, name tag. */
export function placeOverhead(view: ActorView, scaleY: number): void {
  const head = BODY_TOP_M * scaleY + 0.35;
  view.castBar.sprite.position.set(0, head, 0);
  view.resourceBar.sprite.position.set(0, head + 0.07, 0);
  view.healthBar.sprite.position.set(0, head + 0.2, 0);
  view.nameTag.sprite.position.set(0, head + 0.58, 0);
  view.chips.sprite.position.set(0, head + 0.58, 0); // same point as the tag; `center` hangs it below
  view.nose.position.y = BODY_TOP_M * scaleY * 0.62;
}
