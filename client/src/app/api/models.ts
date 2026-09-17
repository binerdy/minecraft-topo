import { Lv95Rect } from '../geo/lv95';

export type SourceKind = 'auto' | 'swissAlti3d' | 'dhm200' | 'synthetic';
export type OutputMode = 'folder' | 'zip';
export type LandCoverKind = 'tlm3d' | 'tlmRegio' | 'vec25';
export type BuildingModelKind = 'swissBuildings3d' | 'footprints';
export type JobState = 'queued' | 'running' | 'done' | 'failed' | 'cancelled';

export interface OverviewStatus {
  phase: string;
  percent: number;
  message: string | null;
  ready: boolean;
}

export interface Defaults {
  savesDir: string | null;
  savesDirExists: boolean;
  cacheBytes: number;
  cacheDir: string;
  dhm200Ready: boolean;
  attribution: string;
  minecraftVersion: string;
  dataVersion: number;
  switzerlandExtent: Lv95Rect;
}

export interface Estimate {
  widthM: number;
  heightM: number;
  blocksWide: number;
  blocksHigh: number;
  blocks: number;
  chunks: number;
  regions: number;
  source: SourceKind;
  tiles: number;
  downloadBytes: number;
  outputBytes: number;
  elevationMin: number | null;
  elevationMax: number | null;
  verticalScale: number | null;
  /** Smallest metres-per-block that keeps true proportions, when the current value squeezes the relief. */
  trueProportionMetresPerBlock: number | null;
  warnings: string[];
  ok: boolean;
}

export interface ProgressInfo {
  phase: string;
  percent: number;
  message: string | null;
  done: number | null;
  total: number | null;
}

export interface GenerationResult {
  outputDir: string;
  blocks: number;
  chunks: number;
  regions: number;
  verticalScale: number;
  minElevation: number;
  maxElevation: number;
  minY: number;
  maxY: number;
  spawnX: number;
  spawnY: number;
  spawnZ: number;
  waterCells: number;
  treeCount: number;
  filledCells: number;
  wroteLevelDat: boolean;
  wroteWorldGenSettings: boolean;
  elapsed: string;
}

export interface JobDto {
  id: string;
  state: JobState;
  worldName: string;
  outputMode: OutputMode;
  progress: ProgressInfo;
  log: string[];
  result: GenerationResult | null;
  error: string | null;
  createdAt: string;
  finishedAt: string | null;
  downloadUrl: string | null;
}

export interface JobRequest {
  area: Lv95Rect;
  worldName: string;
  metresPerBlock: number;
  source: SourceKind;
  alti3dResolution: number;
  baseY: number;
  verticalScale: number | null;
  waterLevel: number | null;
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
  geology: boolean;
  /** Block role overrides: role key -> vanilla block name. */
  blocks: Record<string, string> | null;
  snowLine: number;
  slopeStoneDegrees: number;
  outputMode: OutputMode;
  savesDir: string | null;
  replaceExisting: boolean;
  spawnE: number | null;
  spawnN: number | null;
}

export interface DatasetStatus {
  name: string;
  ready: boolean;
  phase: string;
  percent: number;
  message: string | null;
  approxBytes: number;
}

export interface DatasetsStatus {
  tlm3d: DatasetStatus;
  tlmRegio: DatasetStatus;
}

export type BlockGroup = 'terrain' | 'trees' | 'roads' | 'rails' | 'buildings' | 'power' | 'geology';

export interface BlockRole {
  key: string;
  label: string;
  group: BlockGroup;
  default: string;
}

export interface BlocksInfo {
  roles: BlockRole[];
  choices: string[];
}

export type RegionLevel = 'municipality' | 'district' | 'canton';

export interface RegionInfo {
  name: string;
  level: RegionLevel;
  bounds: Lv95Rect;
  areaKm2: number;
  /** Outer rings as [E, N] pairs. */
  outline: number[][][];
}
