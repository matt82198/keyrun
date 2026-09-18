// Tracks held keyboard codes and which ones were newly pressed since the
// last sample. Operates purely on KeyboardEvent.code strings so it is
// testable without a real DOM/browser.

export class KeyState {
  private held = new Set<string>();
  private justPressed = new Set<string>();

  onKeyDown(code: string): void {
    if (!this.held.has(code)) this.justPressed.add(code);
    this.held.add(code);
  }

  onKeyUp(code: string): void {
    this.held.delete(code);
  }

  isHeld(code: string): boolean {
    return this.held.has(code);
  }

  /** Returns the codes newly pressed since the previous call, then clears them. */
  sampleAndClearJustPressed(): ReadonlySet<string> {
    const sample = this.justPressed;
    this.justPressed = new Set<string>();
    return sample;
  }

  reset(): void {
    this.held.clear();
    this.justPressed.clear();
  }
}
