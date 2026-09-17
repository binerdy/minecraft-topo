/**
 * Approximate WGS84 <-> LV95 (EPSG:2056) conversion, swisstopo formulas (about 1 m accuracy).
 * Mirrors MinecraftTopo.Core/Geo/Lv95.cs.
 */

export interface LatLon {
  lat: number;
  lon: number;
}

export interface Lv95Point {
  e: number;
  n: number;
}

export interface Lv95Rect {
  minE: number;
  minN: number;
  maxE: number;
  maxN: number;
}

export function wgs84ToLv95(lat: number, lon: number): Lv95Point {
  const phi = (lat * 3600 - 169028.66) / 10000;
  const lam = (lon * 3600 - 26782.5) / 10000;
  const phi2 = phi * phi;
  const lam2 = lam * lam;
  const e =
    2600072.37 + 211455.93 * lam - 10938.51 * lam * phi - 0.36 * lam * phi2 - 44.54 * lam2 * lam;
  const n =
    1200147.07 +
    308807.95 * phi +
    3745.25 * lam2 +
    76.63 * phi2 -
    194.56 * lam2 * phi +
    119.79 * phi2 * phi;
  return { e, n };
}

export function lv95ToWgs84(e: number, n: number): LatLon {
  const y = (e - 2600000) / 1000000;
  const x = (n - 1200000) / 1000000;
  const y2 = y * y;
  const x2 = x * x;
  const lam = 2.6779094 + 4.728982 * y + 0.791484 * y * x + 0.1306 * y * x2 - 0.0436 * y2 * y;
  const phi =
    16.9023892 + 3.238272 * x - 0.270978 * y2 - 0.002528 * x2 - 0.0447 * y2 * x - 0.014 * x2 * x;
  return { lat: (phi * 100) / 36, lon: (lam * 100) / 36 };
}

export function rectFromCorners(a: Lv95Point, b: Lv95Point): Lv95Rect {
  return {
    minE: Math.min(a.e, b.e),
    minN: Math.min(a.n, b.n),
    maxE: Math.max(a.e, b.e),
    maxN: Math.max(a.n, b.n),
  };
}

/** Snaps a rectangle to whole multiples of `step` metres and enforces a minimum size. */
export function snapRect(r: Lv95Rect, step: number, minSize = 16): Lv95Rect {
  const s = Math.max(step, 1);
  let minE = Math.round(r.minE / s) * s;
  let minN = Math.round(r.minN / s) * s;
  let maxE = Math.round(r.maxE / s) * s;
  let maxN = Math.round(r.maxN / s) * s;
  const min = Math.max(minSize, s);
  if (maxE - minE < min) maxE = minE + Math.ceil(min / s) * s;
  if (maxN - minN < min) maxN = minN + Math.ceil(min / s) * s;
  return { minE, minN, maxE, maxN };
}

export function rectWidth(r: Lv95Rect): number {
  return r.maxE - r.minE;
}

export function rectHeight(r: Lv95Rect): number {
  return r.maxN - r.minN;
}

/** Corner order: SW, SE, NE, NW. */
export function rectCorners(r: Lv95Rect): Lv95Point[] {
  return [
    { e: r.minE, n: r.minN },
    { e: r.maxE, n: r.minN },
    { e: r.maxE, n: r.maxN },
    { e: r.minE, n: r.maxN },
  ];
}

export function rectsEqual(a: Lv95Rect | null, b: Lv95Rect | null): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  return a.minE === b.minE && a.minN === b.minN && a.maxE === b.maxE && a.maxN === b.maxN;
}
