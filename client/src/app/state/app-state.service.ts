import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService } from '../api/api.service';
import {
  BlockRole,
  BuildingModelKind,
  DatasetsStatus,
  Defaults,
  DifficultyKind,
  Estimate,
  GameModeKind,
  GeologyKind,
  JobDto,
  JobRequest,
  LandCoverKind,
  OutputMode,
  OverviewStatus,
  RegionInfo,
  RegionLevel,
  SourceKind,
  SurfaceStyleKind,
  WorldHeightKind,
} from '../api/models';
import { Lv95Point, Lv95Rect, rectsEqual, snapRect } from '../geo/lv95';

export interface Settings {
  worldName: string;
  metresPerBlock: number;
  source: SourceKind;
  alti3dResolution: number;
  baseY: number;
  worldHeight: WorldHeightKind;
  verticalScaleMode: 'auto' | 'manual';
  verticalScale: number;
  waterEnabled: boolean;
  waterLevel: number;
  waterBodies: boolean;
  trees: boolean;
  vegetation: boolean;
  resources: boolean;
  landCover: LandCoverKind;
  buildingModel: BuildingModelKind;
  roads: boolean;
  rails: boolean;
  buildings: boolean;
  powerLines: boolean;
  villagers: boolean;
  streetSigns: boolean;
  geology: GeologyKind;
  /** 0 = today, else 2010, 1973 or 1850 (Little Ice Age maximum). */
  glacierYear: number;
  iceToBed: boolean;
  lakeFloors: boolean;
  vegetationHeights: boolean;
  surfaceStyle: SurfaceStyleKind;
  placeNames: boolean;
  extras: boolean;
  jura3d: boolean;
  wildlife: boolean;
  crops: boolean;
  streetLights: boolean;
  roofColours: boolean;
  gameMode: GameModeKind;
  difficulty: DifficultyKind;
  /** Block role overrides (role key -> vanilla block name); roles not listed use their default. */
  blocks: Record<string, string>;
  snowLine: number;
  slopeStoneDegrees: number;
  outputMode: OutputMode;
  savesDir: string;
  replaceExisting: boolean;
}

export interface CursorInfo {
  lat: number;
  lon: number;
  e: number;
  n: number;
  elevation: number | null;
}

const DEFAULT_SETTINGS: Settings = {
  worldName: 'Switzerland',
  metresPerBlock: 1,
  source: 'auto',
  alti3dResolution: 2,
  baseY: -60,
  worldHeight: 'auto',
  verticalScaleMode: 'auto',
  verticalScale: 1,
  waterEnabled: false,
  waterLevel: 400,
  waterBodies: true,
  trees: true,
  vegetation: true,
  resources: true,
  landCover: 'tlm3d',
  buildingModel: 'swissBuildings3d',
  roads: true,
  rails: true,
  buildings: true,
  powerLines: true,
  villagers: true,
  streetSigns: true,
  geology: 'gk500',
  glacierYear: 0,
  iceToBed: true,
  lakeFloors: true,
  vegetationHeights: true,
  surfaceStyle: 'photo',
  placeNames: true,
  extras: true,
  jura3d: true,
  wildlife: true,
  crops: true,
  streetLights: true,
  roofColours: true,
  gameMode: 'creative',
  difficulty: 'peaceful',
  blocks: {},
  snowLine: 2500,
  slopeStoneDegrees: 32,
  outputMode: 'folder',
  savesDir: '',
  replaceExisting: true,
};

/** Shared application state as signals. */
@Injectable({ providedIn: 'root' })
export class AppState {
  private readonly api = inject(ApiService);

  readonly defaults = signal<Defaults | null>(null);
  readonly overview = signal<OverviewStatus | null>(null);
  readonly selection = signal<Lv95Rect | null>(null);
  readonly drawMode = signal(false);
  /** Explicit spawn point; null means the centre of the area. */
  readonly spawn = signal<Lv95Point | null>(null);
  readonly spawnMode = signal(false);
  readonly settings = signal<Settings>({ ...DEFAULT_SETTINGS });
  readonly estimate = signal<Estimate | null>(null);
  readonly estimateBusy = signal(false);
  readonly estimateError = signal<string | null>(null);
  readonly job = signal<JobDto | null>(null);
  /** Area of the running or last job, for the map overlay. */
  readonly jobArea = signal<Lv95Rect | null>(null);
  readonly jobError = signal<string | null>(null);
  readonly cursor = signal<CursorInfo | null>(null);
  readonly backendError = signal<string | null>(null);
  /** While true, metres per block is raised automatically so the relief keeps true proportions. */
  readonly autoScale = signal(true);
  /** Explains the last automatic adjustment, if any. */
  readonly scaleNotice = signal<string | null>(null);
  /** Download state of the swisstopo landscape model GeoPackages. */
  readonly datasets = signal<DatasetsStatus | null>(null);
  private datasetTimer: ReturnType<typeof setTimeout> | null = null;
  /** Block roles the user may override and the vanilla blocks on offer. */
  readonly blockRoles = signal<BlockRole[]>([]);
  readonly blockChoices = signal<string[]>([]);
  /** Click-to-select an administrative unit. */
  readonly regionMode = signal(false);
  readonly regionLevel = signal<RegionLevel>('municipality');
  readonly region = signal<RegionInfo | null>(null);
  readonly regionBusy = signal(false);
  readonly regionError = signal<string | null>(null);

  /** The spawn that will be used: explicit or the centre of the selection. */
  readonly effectiveSpawn = computed<Lv95Point | null>(() => {
    const sel = this.selection();
    if (!sel) return null;
    return this.spawn() ?? { e: Math.round((sel.minE + sel.maxE) / 2), n: Math.round((sel.minN + sel.maxN) / 2) };
  });

  readonly jobActive = computed(() => {
    const j = this.job();
    return j !== null && (j.state === 'queued' || j.state === 'running');
  });

  readonly canGenerate = computed(() => {
    const est = this.estimate();
    return this.selection() !== null && est !== null && est.ok && !this.jobActive() && this.settings().worldName.trim().length > 0;
  });

  private estimateTimer: ReturnType<typeof setTimeout> | null = null;
  private jobSub: Subscription | null = null;
  private estimateSeq = 0;

  constructor() {
    // Re-estimate (debounced) whenever the selection or a relevant setting changes.
    effect(() => {
      const sel = this.selection();
      const s = this.settings();
      const key = [s.metresPerBlock, s.source, s.alti3dResolution, s.baseY, s.worldHeight, s.verticalScaleMode, s.verticalScale, s.lakeFloors, s.waterBodies, s.vegetationHeights, s.trees, s.placeNames];
      void key;
      untracked(() => this.scheduleEstimate(sel));
    });
    void this.loadDefaults();
    void this.pollOverview();
    void this.refreshDatasets();
    void this.loadBlocks();
  }

  private async loadBlocks(): Promise<void> {
    try {
      const b = await this.api.getBlocks();
      this.blockRoles.set(b.roles);
      this.blockChoices.set(b.choices);
    } catch {
      /* backend not reachable; loadDefaults reports that */
    }
  }

  /** Sets the block for a role; the role's default removes the override. */
  setBlock(role: string, block: string): void {
    const def = this.blockRoles().find((r) => r.key === role)?.default;
    this.settings.update((s) => {
      const blocks = { ...s.blocks };
      if (!block || block === def) delete blocks[role];
      else blocks[role] = block;
      return { ...s, blocks };
    });
  }

  /** Resolves the administrative unit under a map click and selects its bounding box. */
  async pickRegion(e: number, n: number): Promise<void> {
    this.regionBusy.set(true);
    this.regionError.set(null);
    try {
      const r = await this.api.getRegion(e, n, this.regionLevel());
      this.setSelection(r.bounds, r);
      this.regionMode.set(false);
    } catch (err) {
      this.regionError.set(errorMessage(err));
    } finally {
      this.regionBusy.set(false);
    }
  }

  /** Reads the dataset states; keeps polling while a download is running. */
  async refreshDatasets(): Promise<void> {
    if (this.datasetTimer) clearTimeout(this.datasetTimer);
    try {
      const d = await this.api.getDatasets();
      this.datasets.set(d);
      if (d.tlm3d.phase === 'tlm' || d.tlmRegio.phase === 'tlm') {
        this.datasetTimer = setTimeout(() => void this.refreshDatasets(), 2000);
      }
    } catch {
      /* backend not reachable; loadDefaults reports that */
    }
  }

  async prepareDataset(kind: 'tlm3d' | 'regio'): Promise<void> {
    try {
      await this.api.prepareDataset(kind);
    } finally {
      void this.refreshDatasets();
    }
  }

  async loadDefaults(): Promise<void> {
    try {
      const d = await this.api.getDefaults();
      this.defaults.set(d);
      this.backendError.set(null);
      if (d.savesDir && this.settings().savesDir === '') {
        this.settings.update((s) => ({ ...s, savesDir: d.savesDir ?? '' }));
      }
    } catch {
      this.backendError.set('The backend is not reachable. Start MinecraftTopo.Server and reload.');
    }
  }

  private async pollOverview(): Promise<void> {
    try {
      const st = await this.api.getOverviewStatus();
      this.overview.set(st);
      if (!st.ready && st.phase !== 'error') {
        setTimeout(() => void this.pollOverview(), 1000);
      } else if (st.ready) {
        // A ready model can improve the estimate (elevation range).
        this.scheduleEstimate(this.selection());
      }
    } catch {
      setTimeout(() => void this.pollOverview(), 3000);
    }
  }

  /**
   * Applies a settings change. A metres-per-block value chosen by the user turns the automatic
   * scale adjustment off; automatic changes pass `automatic: true`.
   */
  updateSettings(patch: Partial<Settings>, automatic = false): void {
    this.settings.update((s) => ({ ...s, ...patch }));
    if (patch.metresPerBlock !== undefined) {
      if (!automatic) {
        this.autoScale.set(false);
        this.scaleNotice.set(null);
      }
      const sel = this.selection();
      if (sel) this.setSelection(sel, this.region());
    }
  }

  /** Restores every setting to its default (the saves folder stays), and re-enables automatic scaling. */
  resetSettings(): void {
    const savesDir = this.settings().savesDir || this.defaults()?.savesDir || '';
    this.settings.set({ ...DEFAULT_SETTINGS, savesDir });
    this.autoScale.set(true);
    this.scaleNotice.set(null);
    const sel = this.selection();
    if (sel) this.setSelection(sel, this.region());
  }

  /**
   * Sets the selection, snapped to whole blocks for the current scale. A region passed along
   * keeps its outline on the map; any other change drops the region.
   */
  setSelection(rect: Lv95Rect | null, region: RegionInfo | null = null): void {
    if (rect === null) {
      this.selection.set(null);
      this.spawn.set(null);
      this.spawnMode.set(false);
      this.region.set(null);
      return;
    }
    const snapped = snapRect(rect, this.settings().metresPerBlock);
    if (!rectsEqual(snapped, this.selection())) {
      this.selection.set(snapped);
      this.region.set(region);
    } else if (region) {
      this.region.set(region);
    }
    const sp = this.spawn();
    if (sp) this.spawn.set(clampPoint(sp, snapped));
  }

  /** Sets an explicit spawn point (clamped into the selection); null returns to the centre. */
  setSpawn(p: Lv95Point | null): void {
    const sel = this.selection();
    if (!p || !sel) {
      this.spawn.set(null);
      return;
    }
    this.spawn.set(clampPoint({ e: Math.round(p.e), n: Math.round(p.n) }, sel));
  }

  private scheduleEstimate(sel: Lv95Rect | null): void {
    if (this.estimateTimer) clearTimeout(this.estimateTimer);
    if (!sel) {
      this.estimate.set(null);
      return;
    }
    this.estimateTimer = setTimeout(() => void this.refreshEstimate(sel), 250);
  }

  private async refreshEstimate(sel: Lv95Rect): Promise<void> {
    const seq = ++this.estimateSeq;
    const s = this.settings();
    this.estimateBusy.set(true);
    try {
      const est = await this.api.getEstimate({
        area: sel,
        metresPerBlock: s.metresPerBlock,
        source: s.source,
        alti3dResolution: s.alti3dResolution,
        baseY: s.baseY,
        height: s.worldHeight,
        verticalScale: s.verticalScaleMode === 'manual' ? s.verticalScale : null,
        lakeFloors: s.lakeFloors && s.waterBodies,
        canopy: s.vegetationHeights && s.trees,
        names: s.placeNames,
      });
      if (seq === this.estimateSeq) {
        this.estimate.set(est);
        this.estimateError.set(null);
        const tp = est.trueProportionMetresPerBlock;
        if (this.autoScale() && s.verticalScaleMode === 'auto' && tp !== null && tp > s.metresPerBlock) {
          this.scaleNotice.set(
            `Scale raised from ${s.metresPerBlock} to ${tp} m per block so the relief keeps true proportions ` +
              `(${est.elevationMin} – ${est.elevationMax} m of relief has to fit ${est.availableHeight} blocks). Pick a scale yourself to override, or choose the tall world height to keep 1 m per block.`,
          );
          this.updateSettings({ metresPerBlock: tp }, true);
        }
      }
    } catch (err) {
      if (seq === this.estimateSeq) this.estimateError.set(errorMessage(err));
    } finally {
      if (seq === this.estimateSeq) this.estimateBusy.set(false);
    }
  }

  buildRequest(): JobRequest | null {
    const sel = this.selection();
    if (!sel) return null;
    const s = this.settings();
    return {
      area: sel,
      worldName: s.worldName.trim(),
      metresPerBlock: s.metresPerBlock,
      source: s.source,
      alti3dResolution: s.alti3dResolution,
      baseY: s.baseY,
      worldHeight: s.worldHeight,
      verticalScale: s.verticalScaleMode === 'manual' ? s.verticalScale : null,
      waterLevel: s.waterEnabled ? s.waterLevel : null,
      waterBodies: s.waterBodies,
      trees: s.trees,
      vegetation: s.vegetation,
      resources: s.resources,
      landCover: s.landCover,
      buildingModel: s.buildingModel,
      roads: s.roads,
      rails: s.rails,
      buildings: s.buildings,
      powerLines: s.powerLines,
      villagers: s.villagers,
      streetSigns: s.streetSigns,
      geology: s.geology,
      glacierYear: s.glacierYear,
      iceToBed: s.iceToBed,
      lakeFloors: s.lakeFloors,
      vegetationHeights: s.vegetationHeights,
      surfaceStyle: s.surfaceStyle,
      placeNames: s.placeNames,
      extras: s.extras,
      jura3d: s.jura3d,
      wildlife: s.wildlife,
      crops: s.crops,
      streetLights: s.streetLights,
      roofColours: s.roofColours,
      gameMode: s.gameMode,
      difficulty: s.difficulty,
      blocks: Object.keys(s.blocks).length > 0 ? s.blocks : null,
      snowLine: s.snowLine,
      slopeStoneDegrees: s.slopeStoneDegrees,
      outputMode: s.outputMode,
      savesDir: s.outputMode === 'folder' ? s.savesDir : null,
      replaceExisting: s.replaceExisting,
      spawnE: this.spawn()?.e ?? null,
      spawnN: this.spawn()?.n ?? null,
    };
  }

  async startJob(): Promise<void> {
    const body = this.buildRequest();
    if (!body) return;
    this.jobError.set(null);
    try {
      const job = await this.api.createJob(body);
      this.jobArea.set(body.area);
      this.job.set(job);
      this.jobSub?.unsubscribe();
      this.jobSub = this.api.streamJob(job.id).subscribe({
        next: (dto) => this.job.set(dto),
        error: (err) => this.jobError.set(errorMessage(err)),
        complete: () => void this.finalizeJob(job.id),
      });
    } catch (err) {
      this.jobError.set(errorMessage(err));
    }
  }

  private async finalizeJob(id: string): Promise<void> {
    try {
      const dto = await this.api.getJob(id);
      this.job.set(dto);
    } catch {
      /* keep the last streamed state */
    }
  }

  /** Moves the spawn point of the current job to a block position on its map. */
  async setJobSpawn(x: number, z: number): Promise<void> {
    const j = this.job();
    if (!j) return;
    this.jobError.set(null);
    try {
      const updated = await this.api.setJobSpawn(j.id, x, z);
      this.job.set(updated);
      // keep the S marker on the overview in step with the chosen spawn
      const area = this.jobArea();
      if (area && updated.spawnX !== null && updated.spawnZ !== null) {
        const mpb = this.settings().metresPerBlock;
        this.spawn.set({ e: Math.round(area.minE + (updated.spawnX + 0.5) * mpb), n: Math.round(area.maxN - (updated.spawnZ + 0.5) * mpb) });
      }
    } catch (err) {
      this.jobError.set(errorMessage(err));
    }
  }

  async cancelJob(): Promise<void> {
    const j = this.job();
    if (!j) return;
    try {
      await this.api.cancelJob(j.id);
    } catch (err) {
      this.jobError.set(errorMessage(err));
    }
  }

  clearJob(): void {
    if (this.jobActive()) return;
    this.jobSub?.unsubscribe();
    this.job.set(null);
    this.jobArea.set(null);
    this.jobError.set(null);
  }
}

function clampPoint(p: Lv95Point, r: Lv95Rect): Lv95Point {
  return {
    e: Math.min(Math.max(p.e, r.minE), r.maxE - 1),
    n: Math.min(Math.max(p.n, r.minN + 1), r.maxN),
  };
}

export function errorMessage(err: unknown): string {
  if (err && typeof err === 'object') {
    const e = err as { error?: { error?: string } | string; message?: string; status?: number };
    if (e.error && typeof e.error === 'object' && typeof e.error.error === 'string') return e.error.error;
    if (typeof e.error === 'string' && e.error.length > 0 && e.error.length < 300) return e.error;
    if (e.status === 0) return 'The backend is not reachable.';
    if (typeof e.message === 'string') return e.message;
  }
  return String(err);
}
