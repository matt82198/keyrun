/**
 * Usage: import { installFakeDocument, type FakeElement } from "./dom-stub.js";
 *        const root = installFakeDocument();
 *        renderCharacterPanel(root as unknown as HTMLElement, store, () => {});
 *        expect(root.text()).toContain("Warden");
 *
 * Minimal DOM stub for exercising screen/src/panels/*.ts's render functions under vitest.config.ts's
 * `environment: "node"` (this repo has no jsdom/happy-dom dependency -- package.json is outside this
 * lane's ownership). Implements only the subset of the DOM the panels actually use: createElement,
 * appendChild, textContent (get/set, leaf or aggregated from children), innerHTML reset, className/
 * id/htmlFor/type/value/checked/disabled/selected properties, and addEventListener + a `dispatch`
 * helper so a test can simulate a click/change the way a real user would.
 */
type Listener = () => void;

export class FakeElement {
  readonly tagName: string;
  children: FakeElement[] = [];
  className = "";
  id = "";
  htmlFor = "";
  type = "";
  value = "";
  checked = false;
  disabled = false;
  selected = false;
  private listeners: Record<string, Listener[]> = {};
  private raw = "";

  constructor(tagName: string) {
    this.tagName = tagName.toUpperCase();
  }

  appendChild(child: FakeElement): FakeElement {
    this.children.push(child);
    return child;
  }

  set textContent(value: string) {
    this.raw = value;
    this.children = [];
  }
  get textContent(): string {
    return this.children.length > 0 ? this.children.map((c) => c.textContent).join("") : this.raw;
  }

  set innerHTML(_value: string) {
    this.children = [];
    this.raw = "";
  }

  addEventListener(type: string, fn: Listener): void {
    (this.listeners[type] ??= []).push(fn);
  }

  /** Fires every listener registered for `type` (e.g. "click", "change"), simulating a user action. */
  dispatch(type: string): void {
    for (const fn of this.listeners[type] ?? []) fn();
  }

  /** Depth-first search over this element and its descendants. */
  findAll(pred: (el: FakeElement) => boolean): FakeElement[] {
    const out: FakeElement[] = [];
    const walk = (el: FakeElement): void => {
      if (pred(el)) out.push(el);
      for (const child of el.children) walk(child);
    };
    walk(this);
    return out;
  }

  find(pred: (el: FakeElement) => boolean): FakeElement | undefined {
    return this.findAll(pred)[0];
  }
}

/** Installs a fake `document.createElement` on globalThis and returns a fresh root container. */
export function installFakeDocument(): FakeElement {
  (globalThis as unknown as { document: { createElement: (tag: string) => FakeElement } }).document = {
    createElement: (tag: string) => new FakeElement(tag),
  };
  return new FakeElement("div");
}
