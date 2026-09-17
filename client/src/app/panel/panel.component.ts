import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { BlockGroup, BlockRole } from '../api/models';
import { Lv95Rect } from '../geo/lv95';
import { AppState, Settings } from '../state/app-state.service';

const SCALES = [1, 2, 5, 10, 25, 50, 100];

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
