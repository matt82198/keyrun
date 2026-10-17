// Pure helpers for the `npm run dev` serving log line. Kept out of build.mjs because build.mjs
// runs a top-level build on import.

/** Fallback host when esbuild reports neither `hosts` nor `host`. */
const FALLBACK_HOST = "127.0.0.1";

/**
 * Pick the host to print for an esbuild serve() result.
 * esbuild >= 0.25 returns `hosts` (array); older versions return `host`.
 * @param {{hosts?: string[], host?: string, port?: number}} serveResult
 * @returns {string} never undefined
 */
export function pickServeHost(serveResult) {
  const hosts = serveResult.hosts;
  if (Array.isArray(hosts) && hosts.length > 0 && typeof hosts[0] === "string") return hosts[0];
  if (typeof serveResult.host === "string" && serveResult.host.length > 0) return serveResult.host;
  return FALLBACK_HOST;
}

/**
 * Build the clickable URL for the dev server's client entry, with the fixed dev query string.
 * @param {string} host
 * @param {number} port
 * @returns {string}
 */
export function playableUrl(host, port) {
  return `http://${host}:${port}/client/index.html?key=5&dungeon=hollow-vault&class=warden`;
}
