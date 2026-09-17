import { DecimalPipe } from '@angular/common';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import * as L from 'leaflet';
import { ApiService } from '../api/api.service';
import { Lv95Point, Lv95Rect, lv95ToWgs84, rectCorners, rectFromCorners, wgs84ToLv95 } from '../geo/lv95';
import { RegionInfo } from '../api/models';
import { AppState } from '../state/app-state.service';

const SWITZERLAND_BOUNDS = L.latLngBounds([45.78, 5.9], [47.85, 10.55]);
// The version query busts browser caches when the server-side rendering changes.
const OVERVIEW_URL = '/api/overview/tiles/{z}/{x}/{y}.png?v=3';
const WMTS = 'https://wmts.geo.admin.ch/1.0.0/{layer}/default/current/3857/{z}/{x}/{y}.{ext}';

@Component({
  selector: 'app-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #map class="map" [class.drawing]="state.drawMode()" [class.spawning]="state.spawnMode()" [class.regioning]="state.regionMode()"></div>

    <div class="map-tools">
      <button
        type="button"
        class="draw"
        [class.active]="state.drawMode()"
        (click)="toggleDraw()"
        title="Draw a rectangle on the map"
      >
        {{ state.drawMode() ? 'Drawing… click and drag' : state.selection() ? 'Draw new area' : 'Select area' }}
      </button>
      <div class="region-row">
        <button
          type="button"
          class="region"
          [class.active]="state.regionMode()"
          (click)="toggleRegion()"
          title="Click on the map to select a whole municipality, district or canton (its bounding box becomes the area)"
        >
          {{ state.regionMode() ? (state.regionBusy() ? 'Looking up…' : 'Click on the map…') : 'Select region by click' }}
        </button>
        <select [value]="state.regionLevel()" (change)="state.regionLevel.set($any($event.target).value)" title="Which kind of region a click selects">
          <option value="municipality">Municipality</option>
          <option value="district">District</option>
          <option value="canton">Canton</option>
        </select>
      </div>
      @if (state.regionError(); as err) {
        <span class="region-error">{{ err }}</span>
      }
      @if (state.region(); as r) {
        <span class="region-name">{{ r.level }} <b>{{ r.name }}</b>, {{ r.areaKm2 | number: '1.0-0' }} km²</span>
      }
      @if (state.selection()) {
        <button
          type="button"
          class="spawn"
          [class.active]="state.spawnMode()"
          (click)="toggleSpawn()"
          title="Click on the map inside the area to place the spawn point"
        >
          {{ state.spawnMode() ? 'Click inside the area…' : 'Set spawn point' }}
        </button>
        <button type="button" (click)="zoomToSelection()">Zoom to area</button>
        <button type="button" class="ghost" (click)="clearSelection()">Clear</button>
      }
      <label class="layer">
        <input type="checkbox" [checked]="showMap()" (change)="toggleMap($any($event.target).checked)" />
        National map
        <input
          type="range"
          min="0"
          max="1"
          step="0.05"
          [value]="mapOpacity()"
          (input)="setMapOpacity(+$any($event.target).value)"
          [disabled]="!showMap()"
        />
      </label>
      <label class="layer">
        <input type="checkbox" [checked]="showShade()" (change)="toggleShade($any($event.target).checked)" />
        Hillshade
      </label>
      <label class="layer">
        <input type="checkbox" [checked]="showRoads()" (change)="toggleRoads($any($event.target).checked)" />
        Roads on the overview
      </label>
    </div>

    <div class="readout">
      @if (state.cursor(); as c) {
        <span>E {{ c.e | number: '1.0-0' }} N {{ c.n | number: '1.0-0' }}</span>
        <span>{{ c.lat | number: '1.5-5' }}, {{ c.lon | number: '1.5-5' }}</span>
        <span>{{ c.elevation !== null ? (c.elevation | number: '1.0-0') + ' m' : '—' }}</span>
      } @else {
        <span>Move the mouse over the map</span>
      }
    </div>
  `,
  styles: `
    :host {
      position: relative;
      display: block;
      height: 100%;
      min-height: 300px;
    }
    .map {
      position: absolute;
      inset: 0;
      background: #1c2a3a;
    }
    .map.drawing {
      cursor: crosshair;
    }
    .map-tools {
      position: absolute;
      top: 10px;
      right: 10px;
      z-index: 1000;
      display: flex;
      flex-direction: column;
      gap: 6px;
      background: rgba(255, 255, 255, 0.92);
      padding: 8px;
      border-radius: 6px;
      box-shadow: 0 1px 4px rgba(0, 0, 0, 0.3);
      font-size: 13px;
      min-width: 190px;
    }
    .map-tools button {
      padding: 6px 10px;
      border: 1px solid #888;
      border-radius: 4px;
      background: #fff;
      cursor: pointer;
    }
    .map-tools button.draw.active {
      background: #ff8800;
      color: #fff;
      border-color: #d06f00;
    }
    .map-tools button.ghost {
      background: transparent;
    }
    .map-tools button.spawn.active {
      background: #d32f2f;
      color: #fff;
      border-color: #a52020;
    }
    .map.spawning {
      cursor: crosshair;
    }
    .map.regioning {
      cursor: crosshair;
    }
    .map-tools .region-row {
      display: flex;
      gap: 4px;
    }
    .map-tools .region-row button {
      flex: 1;
    }
    .map-tools .region-row select {
      font: inherit;
      border: 1px solid #888;
      border-radius: 4px;
      background: #fff;
    }
    .map-tools button.region.active {
      background: #1976d2;
      color: #fff;
      border-color: #125ea8;
    }
    .map-tools .region-error {
      color: #b00020;
      max-width: 260px;
      line-height: 1.3;
    }
    .map-tools .region-name {
      color: #333;
      max-width: 260px;
      line-height: 1.3;
    }
    .map-tools .layer {
      display: flex;
      align-items: center;
      gap: 6px;
    }
    .map-tools .layer input[type='range'] {
      width: 70px;
      margin-left: auto;
    }
    .readout {
      position: absolute;
      left: 10px;
      bottom: 24px;
      z-index: 1000;
      display: flex;
      gap: 12px;
      background: rgba(255, 255, 255, 0.9);
      padding: 4px 8px;
      border-radius: 4px;
      font: 12px/1.4 ui-monospace, Consolas, monospace;
      pointer-events: none;
    }
  `,
  imports: [DecimalPipe],
})
export class MapComponent implements AfterViewInit, OnDestroy {
  readonly state = inject(AppState);
  private readonly api = inject(ApiService);
  private readonly mapEl = viewChild.required<ElementRef<HTMLDivElement>>('map');

  readonly showMap = signal(false);
  readonly mapOpacity = signal(0.6);
  readonly showShade = signal(false);
  readonly showRoads = signal(false);

  private map!: L.Map;
  private overviewLayer!: L.TileLayer;
  private mapLayer!: L.TileLayer;
  private shadeLayer!: L.TileLayer;
  private polygon: L.Polygon | null = null;
  private regionOutline: L.Polygon | null = null;
  private handles: L.Marker[] = [];
  private spawnMarker: L.Marker | null = null;
  private drawStart: Lv95Point | null = null;
  private moveStart: { mouse: Lv95Point; rect: Lv95Rect } | null = null;
  private elevationTimer: ReturnType<typeof setTimeout> | null = null;
  private lastRendered: Lv95Rect | null = null;

  constructor() {
    effect(() => {
      const sel = this.state.selection();
      if (this.map) this.renderSelection(sel);
    });
    effect(() => {
      const drawing = this.state.drawMode();
      if (drawing) {
        this.state.spawnMode.set(false);
        this.state.regionMode.set(false);
      }
      if (!this.map) return;
      if (drawing) this.map.dragging.disable();
      else if (!this.moveStart) this.map.dragging.enable();
    });
    effect(() => {
      if (this.state.spawnMode()) {
        this.state.drawMode.set(false);
        this.state.regionMode.set(false);
      }
    });
    effect(() => {
      if (this.state.regionMode()) {
        this.state.drawMode.set(false);
        this.state.spawnMode.set(false);
      }
    });
    effect(() => {
      const r = this.state.region();
      if (this.map) this.renderRegion(r);
    });
    effect(() => {
      const sp = this.state.effectiveSpawn();
      if (this.map) this.renderSpawn(sp);
    });
  }

  ngAfterViewInit(): void {
    this.map = L.map(this.mapEl().nativeElement, {
      minZoom: 5,
      maxZoom: 16,
      zoomSnap: 0.5,
      attributionControl: true,
    });
    this.map.attributionControl.setPrefix('');
    this.map.fitBounds(SWITZERLAND_BOUNDS);

    this.overviewLayer = L.tileLayer(OVERVIEW_URL, {
      minZoom: 5,
      maxZoom: 16,
      className: 'mc-tiles',
      keepBuffer: 4,
      attribution: 'Elevation © <a href="https://www.swisstopo.admin.ch" target="_blank" rel="noopener">swisstopo</a>',
    }).addTo(this.map);

    this.mapLayer = L.tileLayer(WMTS.replace('{layer}', 'ch.swisstopo.pixelkarte-farbe').replace('{ext}', 'jpeg'), {
      maxZoom: 18,
      opacity: this.mapOpacity(),
      attribution: 'Map © swisstopo',
    });
    this.shadeLayer = L.tileLayer(
      WMTS.replace('{layer}', 'ch.swisstopo.swissalti3d-reliefschattierung').replace('{ext}', 'png'),
      { maxZoom: 18, opacity: 0.5 },
    );

    L.control.scale({ imperial: false, maxWidth: 200 }).addTo(this.map);

    this.map.on('mousemove', (ev: L.LeafletMouseEvent) => this.onMouseMove(ev));
    this.map.on('mousedown', (ev: L.LeafletMouseEvent) => this.onMouseDown(ev));
    this.map.on('mouseup', (ev: L.LeafletMouseEvent) => this.onMouseUp(ev));
    this.map.on('mouseout', () => this.state.cursor.set(null));
    this.map.on('click', (ev: L.LeafletMouseEvent) => {
      if (this.state.regionMode()) {
        if (this.state.regionBusy()) return;
        const rp = this.toLv95(ev.latlng);
        void this.state.pickRegion(rp.e, rp.n);
        return;
      }
      if (!this.state.spawnMode()) return;
      const sel = this.state.selection();
      const p = this.toLv95(ev.latlng);
      if (sel && p.e >= sel.minE && p.e <= sel.maxE && p.n >= sel.minN && p.n <= sel.maxN) {
        this.state.setSpawn(p);
        this.state.spawnMode.set(false);
      }
    });
    this.map.getContainer().addEventListener('keydown', (ev) => {
      if (ev.key === 'Escape') {
        this.state.drawMode.set(false);
        this.state.regionMode.set(false);
      }
    });
    // Focus changes can scroll an overflow-hidden container; keep the map pinned.
    const container = this.map.getContainer();
    container.addEventListener('scroll', () => {
      container.scrollTop = 0;
      container.scrollLeft = 0;
    });

    const sel = this.state.selection();
    if (sel) this.renderSelection(sel);
    this.renderSpawn(this.state.effectiveSpawn());
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  // ---- toolbar -------------------------------------------------------------------------------

  toggleDraw(): void {
    this.state.drawMode.update((v) => !v);
  }

  clearSelection(): void {
    this.state.setSelection(null);
    this.state.drawMode.set(false);
  }

  toggleSpawn(): void {
    this.state.spawnMode.update((v) => !v);
  }

  toggleRegion(): void {
    this.state.regionError.set(null);
    this.state.regionMode.update((v) => !v);
  }

  /** Dashed outline of the selected municipality, district or canton (the area is its bounding box). */
  private renderRegion(r: RegionInfo | null): void {
    this.regionOutline?.remove();
    this.regionOutline = null;
    if (!r) return;
    const rings = r.outline.map((ring) =>
      ring.map(([e, n]) => {
        const ll = lv95ToWgs84(e, n);
        return L.latLng(ll.lat, ll.lon);
      }),
    );
    this.regionOutline = L.polygon(rings, {
      color: '#1976d2',
      weight: 2,
      dashArray: '6 4',
      fill: true,
      fillColor: '#1976d2',
      fillOpacity: 0.08,
      interactive: false,
    }).addTo(this.map);
    this.map.fitBounds(this.regionOutline.getBounds(), { padding: [40, 40] });
  }

  zoomToSelection(): void {
    const sel = this.state.selection();
    if (!sel) return;
    this.map.fitBounds(this.toLatLngs(sel), { padding: [40, 40] });
  }

  toggleMap(on: boolean): void {
    this.showMap.set(on);
    if (on) this.mapLayer.addTo(this.map);
    else this.mapLayer.remove();
  }

  setMapOpacity(v: number): void {
    this.mapOpacity.set(v);
    this.mapLayer.setOpacity(v);
  }

  toggleShade(on: boolean): void {
    this.showShade.set(on);
    if (on) this.shadeLayer.addTo(this.map);
    else this.shadeLayer.remove();
  }

  toggleRoads(on: boolean): void {
    this.showRoads.set(on);
    this.overviewLayer.setUrl(on ? OVERVIEW_URL + '&roads=true' : OVERVIEW_URL);
  }


  // ---- mouse handling -----------------------------------------------------------------------

  private onMouseDown(ev: L.LeafletMouseEvent): void {
    if (!this.state.drawMode()) return;
    L.DomEvent.stop(ev.originalEvent);
    this.drawStart = this.toLv95(ev.latlng);
  }

  private onMouseMove(ev: L.LeafletMouseEvent): void {
    const p = this.toLv95(ev.latlng);
    this.updateCursor(ev.latlng, p);

    if (this.drawStart) {
      this.previewRect(rectFromCorners(this.drawStart, p));
    } else if (this.moveStart) {
      const de = p.e - this.moveStart.mouse.e;
      const dn = p.n - this.moveStart.mouse.n;
      const r = this.moveStart.rect;
      this.previewRect({ minE: r.minE + de, minN: r.minN + dn, maxE: r.maxE + de, maxN: r.maxN + dn });
    }
  }

  private onMouseUp(ev: L.LeafletMouseEvent): void {
    const p = this.toLv95(ev.latlng);
    if (this.drawStart) {
      const rect = rectFromCorners(this.drawStart, p);
      this.drawStart = null;
      this.state.drawMode.set(false);
      if (rect.maxE - rect.minE > 5 && rect.maxN - rect.minN > 5) this.state.setSelection(rect);
      else this.renderSelection(this.state.selection());
    } else if (this.moveStart) {
      const de = p.e - this.moveStart.mouse.e;
      const dn = p.n - this.moveStart.mouse.n;
      const r = this.moveStart.rect;
      this.moveStart = null;
      this.map.dragging.enable();
      this.state.setSelection({ minE: r.minE + de, minN: r.minN + dn, maxE: r.maxE + de, maxN: r.maxN + dn });
      this.renderSelection(this.state.selection());
    }
  }

  private updateCursor(latlng: L.LatLng, p: Lv95Point): void {
    const prev = this.state.cursor();
    this.state.cursor.set({ lat: latlng.lat, lon: latlng.lng, e: p.e, n: p.n, elevation: prev?.elevation ?? null });
    if (this.elevationTimer) clearTimeout(this.elevationTimer);
    this.elevationTimer = setTimeout(async () => {
      try {
        const elevation = await this.api.getElevation(p.e, p.n);
        const cur = this.state.cursor();
        if (cur && Math.abs(cur.e - p.e) < 500 && Math.abs(cur.n - p.n) < 500) {
          this.state.cursor.set({ ...cur, elevation });
        }
      } catch {
        /* ignore */
      }
    }, 150);
  }

  // ---- selection rendering ---------------------------------------------------------------------

  private previewRect(rect: Lv95Rect): void {
    if (!this.polygon) {
      this.polygon = L.polygon(this.toLatLngs(rect), this.polygonStyle()).addTo(this.map);
    } else {
      this.polygon.setLatLngs(this.toLatLngs(rect));
    }
    this.positionHandles(rect);
  }

  private renderSelection(rect: Lv95Rect | null): void {
    this.lastRendered = rect;
    if (!rect) {
      this.polygon?.remove();
      this.polygon = null;
      this.handles.forEach((h) => h.remove());
      this.handles = [];
      return;
    }
    if (!this.polygon) {
      this.polygon = L.polygon(this.toLatLngs(rect), this.polygonStyle()).addTo(this.map);
      this.polygon.on('mousedown', (ev: L.LeafletMouseEvent) => {
        if (this.state.drawMode()) return;
        const sel = this.state.selection();
        if (!sel) return;
        L.DomEvent.stop(ev.originalEvent);
        this.map.dragging.disable();
        this.moveStart = { mouse: this.toLv95(ev.latlng), rect: sel };
      });
    } else {
      this.polygon.setLatLngs(this.toLatLngs(rect));
    }
    if (this.handles.length === 0) {
      for (let i = 0; i < 4; i++) {
        const marker = L.marker([0, 0], {
          draggable: true,
          icon: L.divIcon({ className: 'sel-handle', iconSize: [14, 14] }),
          keyboard: false,
        }).addTo(this.map);
        marker.on('drag', () => this.onHandleDrag(i, marker.getLatLng(), false));
        marker.on('dragend', () => this.onHandleDrag(i, marker.getLatLng(), true));
        this.handles.push(marker);
      }
    }
    this.positionHandles(rect);
  }

  private onHandleDrag(index: number, latlng: L.LatLng, final: boolean): void {
    const sel = this.lastRendered;
    if (!sel) return;
    const corners = rectCorners(sel);
    const opposite = corners[(index + 2) % 4];
    const rect = rectFromCorners(opposite, this.toLv95(latlng));
    if (final) {
      this.state.setSelection(rect);
      this.renderSelection(this.state.selection());
    } else {
      this.previewRect(rect);
    }
  }

  private renderSpawn(p: Lv95Point | null): void {
    if (!p) {
      this.spawnMarker?.remove();
      this.spawnMarker = null;
      return;
    }
    const ll = lv95ToWgs84(p.e, p.n);
    if (!this.spawnMarker) {
      this.spawnMarker = L.marker([ll.lat, ll.lon], {
        draggable: true,
        icon: L.divIcon({ className: 'spawn-marker', iconSize: [22, 22], iconAnchor: [11, 11], html: '<span>S</span>' }),
        title: 'Spawn point (drag to move)',
        zIndexOffset: 1000,
        keyboard: false,
      }).addTo(this.map);
      this.spawnMarker.on('dragend', () => {
        const m = this.spawnMarker;
        if (m) this.state.setSpawn(this.toLv95(m.getLatLng()));
      });
    } else {
      this.spawnMarker.setLatLng([ll.lat, ll.lon]);
    }
  }

  private positionHandles(rect: Lv95Rect): void {
    const corners = rectCorners(rect);
    this.handles.forEach((h, i) => {
      const c = lv95ToWgs84(corners[i].e, corners[i].n);
      h.setLatLng([c.lat, c.lon]);
    });
  }

  private polygonStyle(): L.PolylineOptions {
    return { color: '#ff8800', weight: 2, fillColor: '#ffaa33', fillOpacity: 0.12, interactive: true };
  }

  private toLatLngs(rect: Lv95Rect): L.LatLngTuple[] {
    return rectCorners(rect).map((c): L.LatLngTuple => {
      const p = lv95ToWgs84(c.e, c.n);
      return [p.lat, p.lon];
    });
  }

  private toLv95(latlng: L.LatLng): Lv95Point {
    return wgs84ToLv95(latlng.lat, latlng.lng);
  }
}
