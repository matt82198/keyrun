// Floating damage number text sprites. Spawned by main.ts whenever an
// actor's health drops between snapshots; they drift upward and fade out.
import * as THREE from "three";

interface FloatingNumber {
  sprite: THREE.Sprite;
  ageSeconds: number;
}

const LIFETIME_SECONDS = 1.1;
const RISE_METERS = 1.2;

function buildSprite(amount: number): THREE.Sprite {
  const canvas = document.createElement("canvas");
  canvas.width = 160;
  canvas.height = 64;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("2d canvas context unavailable");
  ctx.font = "bold 40px sans-serif";
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.lineWidth = 6;
  ctx.strokeStyle = "rgba(0,0,0,0.85)";
  ctx.fillStyle = "#ff6b6b";
  const text = String(Math.round(amount));
  ctx.strokeText(text, canvas.width / 2, canvas.height / 2);
  ctx.fillText(text, canvas.width / 2, canvas.height / 2);

  const texture = new THREE.CanvasTexture(canvas);
  const material = new THREE.SpriteMaterial({ map: texture, transparent: true, depthTest: false });
  const sprite = new THREE.Sprite(material);
  sprite.scale.set(1.2, 0.5, 1);
  sprite.renderOrder = 20;
  return sprite;
}

export class DamageNumberField {
  private active: FloatingNumber[] = [];

  constructor(private readonly scene: THREE.Scene) {}

  spawn(worldX: number, worldY: number, worldZ: number, amount: number): void {
    if (amount <= 0) return;
    const sprite = buildSprite(amount);
    sprite.position.set(worldX + (Math.random() - 0.5) * 0.4, worldY, worldZ);
    this.scene.add(sprite);
    this.active.push({ sprite, ageSeconds: 0 });
  }

  update(dtSeconds: number): void {
    for (const entry of this.active) {
      entry.ageSeconds += dtSeconds;
      const t = entry.ageSeconds / LIFETIME_SECONDS;
      entry.sprite.position.y += (RISE_METERS * dtSeconds) / LIFETIME_SECONDS;
      const material = entry.sprite.material as THREE.SpriteMaterial;
      material.opacity = Math.max(0, 1 - t);
    }
    for (const entry of this.active) {
      if (entry.ageSeconds >= LIFETIME_SECONDS) {
        this.scene.remove(entry.sprite);
        (entry.sprite.material as THREE.SpriteMaterial).map?.dispose();
        entry.sprite.material.dispose();
      }
    }
    this.active = this.active.filter((entry) => entry.ageSeconds < LIFETIME_SECONDS);
  }
}
