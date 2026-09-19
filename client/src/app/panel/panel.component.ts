import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, effect, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { BlockGroup, BlockRole, JobDto } from '../api/models';
import { Lv95Rect } from '../geo/lv95';
import { AppState, Settings } from '../state/app-state.service';

const SCALES = [0.5, 1, 2, 5, 10, 25, 50, 100];

@Component({
  selector: 'app-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, NgTemplateOutlet],
  templateUrl: './panel.component.html',
  styleUrl: './panel.component.css',
})
export class PanelComponent {
  readonly state = inject(AppState);
  private readonly api = inject(ApiService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly scales = SCALES;

  constructor() {
    // Bring the job card into view when a job starts.
    let lastId: string | null = null;
    effect(() => {
      const j = this.state.job();
      if (j && j.id !== lastId) {
        lastId = j.id;
        setTimeout(() => this.host.nativeElement.querySelector('section.job')?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
      }
    });
  }

  readonly selection = this.state.selection;
  readonly settings = this.state.settings;
  readonly estimate = this.state.estimate;
  readonly job = this.state.job;

  readonly infrastructureAvailable = computed(() => this.settings().landCover !== 'vec25');

  readonly areaKm = computed(() => {
    const s = this.selection();
    if (!s) return null;
    return { w: (s.maxE - s.minE) / 1000, h: (s.maxN - s.minN) / 1000 };
  });

  readonly sourceLabel = computed(() => {
    const est = this.estimate();
    const s = this.settings();
    const src = est?.source ?? s.source;
    switch (src) {
      case 'swissAlti3d':
        return `swissALTI3D ${s.alti3dResolution} m`;
      case 'dhm200':
        return 'country model 200 m';
      case 'synthetic':
        return 'synthetic test terrain';
      default:
        return 'auto';
    }
  });

  readonly progressPercent = computed(() => {
    const j = this.job();
    if (!j) return 0;
    return Math.max(0, Math.min(100, j.progress.percent));
  });

  readonly logLines = computed(() => {
    const j = this.job();
    return j ? j.log.slice(-60) : [];
  });

  /** Newest first, for a column-reverse list that stays scrolled to the latest line. */
  readonly logLinesReversed = computed(() => {
    const j = this.job();
    return j ? j.log.slice(-120).reverse() : [];
  });

  update<K extends keyof Settings>(key: K, value: Settings[K]): void {
    this.state.updateSettings({ [key]: value } as Partial<Settings>);
  }

  // ---- block choices ---------------------------------------------------------------------------

  private readonly openGroups = signal<Record<string, boolean>>({});

  rolesFor(group: BlockGroup): BlockRole[] {
    return this.state.blockRoles().filter((r) => r.group === group);
  }

  blockValue(role: BlockRole): string {
    return this.settings().blocks[role.key] ?? role.default;
  }

  setBlock(key: string, block: string): void {
    this.state.setBlock(key, block);
  }

  overrideCount(group: BlockGroup): number {
    const blocks = this.settings().blocks;
    return this.rolesFor(group).filter((r) => blocks[r.key] !== undefined).length;
  }

  resetGroup(group: BlockGroup): void {
    for (const r of this.rolesFor(group)) this.state.setBlock(r.key, r.default);
  }

  isOpen(group: string): boolean {
    return this.openGroups()[group] === true;
  }

  setOpen(group: string, open: boolean): void {
    this.openGroups.update((g) => ({ ...g, [group]: open }));
  }

  number(ev: Event): number {
    const v = parseFloat((ev.target as HTMLInputElement).value);
    return Number.isFinite(v) ? v : 0;
  }

  int(ev: Event): number {
    return Math.round(this.number(ev));
  }

  text(ev: Event): string {
    return (ev.target as HTMLInputElement).value;
  }

  checked(ev: Event): boolean {
    return (ev.target as HTMLInputElement).checked;
  }

  setScale(ev: Event): void {
    const v = parseFloat((ev.target as HTMLSelectElement).value);
    if (Number.isFinite(v) && v > 0) this.update('metresPerBlock', v);
  }

  setRect(field: keyof Lv95Rect | 'width' | 'height', ev: Event): void {
    const sel = this.selection();
    const v = this.number(ev);
    if (!sel || !Number.isFinite(v)) return;
    const r: Lv95Rect = { ...sel };
    switch (field) {
      case 'minE':
        r.maxE = v + (sel.maxE - sel.minE);
        r.minE = v;
        break;
      case 'minN':
        r.maxN = v + (sel.maxN - sel.minN);
        r.minN = v;
        break;
      case 'width':
        r.maxE = sel.minE + Math.max(v, 16);
        break;
      case 'height':
        r.maxN = sel.minN + Math.max(v, 16);
        break;
      default:
        return;
    }
    this.state.setSelection(r);
  }

  setSpawn(axis: 'e' | 'n', ev: Event): void {
    const cur = this.state.effectiveSpawn();
    const v = this.number(ev);
    if (!cur || !Number.isFinite(v)) return;
    this.state.setSpawn(axis === 'e' ? { e: v, n: cur.n } : { e: cur.e, n: v });
  }

  generate(): void {
    void this.state.startJob();
  }

  // ---- live map ----------------------------------------------------------------------------------

  readonly fullscreen = signal(false);
  /** Stage chosen with the chips; null follows the playback. */
  private readonly selectedStage = signal<string | null>(null);
  /** Stages that arrived but were not shown yet; each gets StageMillis on screen. */
  private readonly pending = signal<string[]>([]);
  /** The stage the playback currently shows (null before the first snapshot). */
  private readonly playhead = signal<string | null>(null);
  /** The previous picture, kept under the new one while it fades in. */
  readonly previousSrc = signal<string | null>(null);
  private lastShownSrc: string | null = null;
  private playTimer: ReturnType<typeof setTimeout> | null = null;
  private seenStages = 0;
  private seenJobId: string | null = null;
  private static readonly StageMillis = 1600;

  // Feeds every new snapshot into the playback queue and opens the full-screen view when the first one arrives.
  private readonly playbackEffect = effect(() => {
      const j = this.job();
      if (!j) {
        this.resetPlayback();
        return;
      }
      if (j.id !== this.seenJobId) {
        this.resetPlayback();
        this.seenJobId = j.id;
      }
      if (j.mapStages.length > this.seenStages) {
        const fresh = j.mapStages.slice(this.seenStages);
        this.seenStages = j.mapStages.length;
        this.pending.update((p) => [...p, ...fresh]);
        if (!this.playTimer) this.advance();
        if (this.seenStages === fresh.length) this.fullscreen.set(true);
      }
  });

  private resetPlayback(): void {
    if (this.playTimer) clearTimeout(this.playTimer);
    this.playTimer = null;
    this.pending.set([]);
    this.playhead.set(null);
    this.previousSrc.set(null);
    this.lastShownSrc = null;
    this.selectedStage.set(null);
    this.seenStages = 0;
    this.seenJobId = null;
  }

  /** Shows the next queued stage and schedules the one after it. */
  private advance(): void {
    this.playTimer = null;
    const queue = this.pending();
    if (queue.length === 0) return;
    this.playhead.set(queue[0]);
    this.pending.set(queue.slice(1));
    this.playTimer = setTimeout(() => this.advance(), PanelComponent.StageMillis);
  }

  isPending(stage: string): boolean {
    return this.pending().includes(stage);
  }

  stageShown(job: JobDto): string | null {
    const s = this.selectedStage();
    if (s && job.mapStages.includes(s)) return s;
    return this.playhead() ?? job.mapStage;
  }

  selectStage(stage: string, job: JobDto): void {
    // choosing the newest stage means "follow along" again
    this.selectedStage.set(stage === job.mapStage ? null : stage);
  }

  mapSrc(job: JobDto): string {
    const s = this.stageShown(job);
    if (s && s !== job.mapStage && job.mapStages.includes(s)) return `/api/jobs/${job.id}/map.png?stage=${encodeURIComponent(s)}&v=${job.mapStages.length}`;
    return job.mapUrl ?? '';
  }

  /** Called when a new picture has loaded: it becomes the backdrop for the next fade. */
  shown(src: string): void {
    if (this.lastShownSrc && this.lastShownSrc !== src) this.previousSrc.set(this.lastShownSrc);
    this.lastShownSrc = src;
  }

  @HostListener('document:keydown.escape')
  closeFullscreen(): void {
    this.fullscreen.set(false);
  }

  // ---- zoom and pan in full screen ---------------------------------------------------------------

  readonly zoom = signal(1);
  readonly panX = signal(0);
  readonly panY = signal(0);
  readonly dragActive = signal(false);
  readonly frameTransform = computed(() => `translate(${this.panX()}px, ${this.panY()}px) scale(${this.zoom()})`);
  private dragStartX = 0;
  private dragStartY = 0;
  private panStartX = 0;
  private panStartY = 0;
  private dragMoved = false;
  private readonly resetViewOnToggle = effect(() => {
    this.fullscreen();
    this.zoom.set(1);
    this.panX.set(0);
    this.panY.set(0);
  });

  onWheel(ev: WheelEvent): void {
    if (!this.fullscreen()) return;
    ev.preventDefault();
    const frame = (ev.currentTarget as HTMLElement).querySelector('.frame') as HTMLElement | null;
    if (!frame) return;
    const rect = frame.getBoundingClientRect();
    const zoom = this.zoom();
    const next = Math.min(40, Math.max(1, zoom * Math.exp(-ev.deltaY * 0.0015)));
    if (next === zoom) return;
    // keep the point under the cursor where it is
    const fx = (ev.clientX - rect.left) / zoom;
    const fy = (ev.clientY - rect.top) / zoom;
    this.panX.set(this.panX() + (ev.clientX - rect.left) - fx * next);
    this.panY.set(this.panY() + (ev.clientY - rect.top) - fy * next);
    this.zoom.set(next);
    if (next === 1) {
      this.panX.set(0);
      this.panY.set(0);
    }
  }

  dragStart(ev: MouseEvent): void {
    if (!this.fullscreen() || ev.button !== 0) return;
    this.dragActive.set(true);
    this.dragMoved = false;
    this.dragStartX = ev.clientX;
    this.dragStartY = ev.clientY;
    this.panStartX = this.panX();
    this.panStartY = this.panY();
  }

  dragMove(ev: MouseEvent): void {
    if (!this.dragActive()) return;
    const dx = ev.clientX - this.dragStartX;
    const dy = ev.clientY - this.dragStartY;
    if (Math.abs(dx) > 4 || Math.abs(dy) > 4) this.dragMoved = true;
    if (this.dragMoved) {
      this.panX.set(this.panStartX + dx);
      this.panY.set(this.panStartY + dy);
    }
  }

  dragEnd(): void {
    this.dragActive.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  onKey(ev: KeyboardEvent): void {
    if (!this.fullscreen()) return;
    const target = ev.target as HTMLElement | null;
    if (target && (target.tagName === 'INPUT' || target.tagName === 'SELECT' || target.tagName === 'TEXTAREA')) return;
    const step = 80;
    switch (ev.key) {
      case 'ArrowLeft': this.panX.update((v) => v + step); break;
      case 'ArrowRight': this.panX.update((v) => v - step); break;
      case 'ArrowUp': this.panY.update((v) => v + step); break;
      case 'ArrowDown': this.panY.update((v) => v - step); break;
      case '+': case '=': this.zoom.update((z) => Math.min(40, z * 1.25)); break;
      case '-': this.zoom.update((z) => Math.max(1, z / 1.25)); break;
      case '0': this.zoom.set(1); this.panX.set(0); this.panY.set(0); break;
      default: return;
    }
    ev.preventDefault();
  }

  chunksDone(job: JobDto): number {
    return this.chunkBytes(job).reduce((n, b) => n + (b ? 1 : 0), 0);
  }

  private chunkBytes(job: JobDto): Uint8Array {
    if (!job.chunkMask) return new Uint8Array(0);
    const bin = atob(job.chunkMask);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
  }

  /** Dark veil over the chunks not written yet, as a tiny image scaled over the map. */
  readonly chunkOverlay = computed<string | null>(() => {
    const j = this.job();
    if (!j || !j.chunkMask || j.state === 'done') return null;
    const bytes = this.chunkBytes(j);
    if (bytes.length !== j.chunksWide * j.chunksHigh) return null;
    const canvas = document.createElement('canvas');
    canvas.width = j.chunksWide;
    canvas.height = j.chunksHigh;
    const ctx = canvas.getContext('2d');
    if (!ctx) return null;
    const img = ctx.createImageData(j.chunksWide, j.chunksHigh);
    for (let i = 0; i < bytes.length; i++) {
      img.data[i * 4 + 3] = bytes[i] ? 0 : 150;
    }
    ctx.putImageData(img, 0, 0);
    return canvas.toDataURL();
  });

  /** Spawn of a job in block and LV95 coordinates, or null while unknown. */
  spawnInfo(job: JobDto): { x: number; y: number; z: number; e: number; n: number } | null {
    if (job.spawnX === null || job.spawnZ === null) return null;
    const sel = this.selection();
    const mpb = this.settings().metresPerBlock;
    return {
      x: job.spawnX,
      y: job.spawnY ?? 0,
      z: job.spawnZ,
      e: sel ? sel.minE + job.spawnX * mpb : 0,
      n: sel ? sel.maxN - job.spawnZ * mpb : 0,
    };
  }

  /** Converts a click on the job map into block coordinates and moves the spawn there. */
  mapClick(ev: MouseEvent, job: JobDto): void {
    if (this.dragMoved) {
      this.dragMoved = false;
      return;
    }
    if (!job.spawnEditable) return;
    const box = (ev.currentTarget as HTMLElement).querySelector('img');
    if (!box) return;
    const rect = box.getBoundingClientRect();
    const fx = (ev.clientX - rect.left) / rect.width;
    const fz = (ev.clientY - rect.top) / rect.height;
    if (fx < 0 || fz < 0 || fx > 1 || fz > 1) return;
    void this.state.setJobSpawn(Math.floor(fx * job.blocksWide), Math.floor(fz * job.blocksHigh));
  }

  cancel(): void {
    void this.state.cancelJob();
  }

  dismissJob(): void {
    this.state.clearJob();
  }

  reveal(): void {
    const j = this.job();
    if (j) void this.api.revealJob(j.id);
  }

  async clearCache(): Promise<void> {
    await this.api.clearCache();
    await this.state.loadDefaults();
  }

  mb(bytes: number): string {
    if (bytes < 1_000_000) return `${(bytes / 1000).toFixed(0)} kB`;
    if (bytes < 1_000_000_000) return `${(bytes / 1_000_000).toFixed(1)} MB`;
    return `${(bytes / 1_000_000_000).toFixed(2)} GB`;
  }
}
