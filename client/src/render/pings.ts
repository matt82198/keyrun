// Ping markers: a floor decal at the ping location plus a 3-second text
// label above it -- the game turning a G-press into party language
// (design/BIBLE.md §10: "kick", "stack here", "Window!"). Placeholder
// geometry/text only, per client/CLAUDE.md.
//
// Split like telegraphFill.ts/telegraphs.ts: pingLabel.ts holds the pure,
// unit-tested label/expiry rules; PingMarkerPool below is the Three.js-
// owning class that uses them and is exercised only by the project's
// headless smoke test / manual screenshot pass, same as TelegraphPool and
// DamageNumberField (neither of which is unit tested either).
import * as THREE from "three";
import { createCanvasSprite, type CanvasSprite } from "./canvasSprite.js";
import type { PingEvent } from "../input/ping.js";
import { isPingExpired, pingLifeRemainingFraction } from "./pingLabel.js";

export { PING_LABEL_LIFETIME_TICKS, pingLabelText, isPingExpired, pingLifeRemainingFraction } from "./pingLabel.js";
export type { PingLabelContext } from "./pingLabel.js";

function buildLabelSprite(text: string): CanvasSprite {
  const sprite = createCanvasSprite(256, 96, 2.2, 0.8);
  sprite.ctx.font = "bold 44px sans-serif";
  sprite.ctx.textAlign = "center";
  sprite.ctx.textBaseline = "middle";
  sprite.ctx.lineWidth = 6;
  sprite.ctx.strokeStyle = "rgba(0,0,0,0.85)";
  sprite.ctx.fillStyle = "#ffe066";
  sprite.ctx.strokeText(text, sprite.canvas.width / 2, sprite.canvas.height / 2);
  sprite.ctx.fillText(text, sprite.canvas.width / 2, sprite.canvas.height / 2);
  sprite.sync();
  return sprite;
}

function buildMarkerMesh(): THREE.Mesh {
  const geometry = new THREE.RingGeometry(0.35, 0.55, 32);
  const material = new THREE.MeshBasicMaterial({
    color: 0xffe066,
    transparent: true,
    opacity: 0.9,
    side: THREE.DoubleSide,
    depthWrite: false,
  });
  const mesh = new THREE.Mesh(geometry, material);
  mesh.rotation.x = -Math.PI / 2;
  mesh.renderOrder = 3;
  return mesh;
}

interface PingView {
  marker: THREE.Mesh;
  markerMaterial: THREE.MeshBasicMaterial;
  label: CanvasSprite;
}

/** World-space placement a ping marker/label should render at. */
export interface PingWorldPlacement {
  x: number;
  y: number;
  z: number;
  /** Height (metres) the floating label sits above the floor marker. */
  labelHeight?: number;
}

/**
 * Pool of live ping markers, reconciled each frame from the current
 * PingEvent list -- same shape as TelegraphPool.sync(). Callers supply the
 * world placement and label text per event (camera/target lookups live
 * upstream, out of scope here).
 */
export class PingMarkerPool {
  private views = new Map<string, PingView>();

  constructor(private readonly scene: THREE.Scene) {}

  private static keyFor(event: PingEvent): string {
    return `${event.actorId}:${event.tick}`;
  }

  sync(
    pings: readonly PingEvent[],
    currentTick: number,
    placementFor: (event: PingEvent) => PingWorldPlacement | null,
    labelTextFor: (event: PingEvent) => string,
  ): void {
    const seen = new Set<string>();
    for (const event of pings) {
      if (isPingExpired(event, currentTick)) continue;
      const placement = placementFor(event);
      if (!placement) continue;
      const key = PingMarkerPool.keyFor(event);
      seen.add(key);

      let view = this.views.get(key);
      if (!view) {
        const marker = buildMarkerMesh();
        const label = buildLabelSprite(labelTextFor(event));
        this.scene.add(marker);
        this.scene.add(label.sprite);
        view = { marker, markerMaterial: marker.material as THREE.MeshBasicMaterial, label };
        this.views.set(key, view);
      }

      view.marker.position.set(placement.x, 0.04, placement.z);
      view.label.sprite.position.set(placement.x, placement.y + (placement.labelHeight ?? 2.2), placement.z);

      const fraction = pingLifeRemainingFraction(event, currentTick);
      view.markerMaterial.opacity = 0.25 + fraction * 0.65;
    }

    for (const [key, view] of this.views) {
      if (seen.has(key)) continue;
      this.scene.remove(view.marker);
      view.marker.geometry.dispose();
      view.markerMaterial.dispose();
      this.scene.remove(view.label.sprite);
      (view.label.sprite.material as THREE.SpriteMaterial).map?.dispose();
      view.label.sprite.material.dispose();
      this.views.delete(key);
    }
  }
}
