import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService } from '../api/api.service';
import { Defaults, Estimate, JobDto, JobRequest, OutputMode, OverviewStatus, SourceKind } from '../api/models';
import { Lv95Point, Lv95Rect, rectsEqual, snapRect } from '../geo/lv95';

export interface Settings {
  worldName: string;
  metresPerBlock: number;
  source: SourceKind;
  alti3dResolution: number;
  baseY: number;
  verticalScaleMode: 'auto' | 'manual';
  verticalScale: number;
  waterEnabled: boolean;
  waterLevel: number;
  waterBodies: boolean;
  trees: boolean;
  vegetation: boolean;
  resources: boolean;
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
  baseY: 0,
  verticalScaleMode: 'auto',
  verticalScale: 1,
  waterEnabled: false,
  waterLevel: 400,
  waterBodies: true,
  trees: true,
  vegetation: true,
  resources: true,
  snowLine: 2500,
  slopeStoneDegrees: 32,
  outputMode: 'folder',
  savesDir: '',
  replaceExisting: false,
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
  readonly jobError = signal<string | null>(null);
  readonly cursor = signal<CursorInfo | null>(null);
  readonly backendError = signal<string | null>(null);

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
      const key = [s.metresPerBlock, s.source, s.alti3dResolution, s.baseY, s.verticalScaleMode, s.verticalScale];
      void key;
      untracked(() => this.scheduleEstimate(sel));
    });
    void this.loadDefaults();
    void this.pollOverview();
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

  updateSettings(patch: Partial<Settings>): void {
    this.settings.update((s) => ({ ...s, ...patch }));
    if (patch.metresPerBlock !== undefined) {
      const sel = this.selection();
      if (sel) this.setSelection(sel);
    }
  }

  /** Sets the selection, snapped to whole blocks for the current scale. */
  setSelection(rect: Lv95Rect | null): void {
    if (rect === null) {
      this.selection.set(null);
      this.spawn.set(null);
      this.spawnMode.set(false);
      return;
    }
    const snapped = snapRect(rect, this.settings().metresPerBlock);
    if (!rectsEqual(snapped, this.selection())) this.selection.set(snapped);
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
        verticalScale: s.verticalScaleMode === 'manual' ? s.verticalScale : null,
      });
      if (seq === this.estimateSeq) {
        this.estimate.set(est);
        this.estimateError.set(null);
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
      verticalScale: s.verticalScaleMode === 'manual' ? s.verticalScale : null,
      waterLevel: s.waterEnabled ? s.waterLevel : null,
      waterBodies: s.waterBodies,
      trees: s.trees,
      vegetation: s.vegetation,
      resources: s.resources,
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
