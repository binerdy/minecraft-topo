import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { Lv95Rect } from '../geo/lv95';
import { BlocksInfo, DatasetStatus, DatasetsStatus, Defaults, Estimate, JobDto, JobRequest, OverviewStatus, RegionInfo, RegionLevel, SourceKind } from './models';

export interface EstimateParams {
  area: Lv95Rect;
  metresPerBlock: number;
  source: SourceKind;
  alti3dResolution: number;
  baseY: number;
  verticalScale: number | null;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  getDefaults(): Promise<Defaults> {
    return firstValueFrom(this.http.get<Defaults>('/api/defaults'));
  }

  getOverviewStatus(): Promise<OverviewStatus> {
    return firstValueFrom(this.http.get<OverviewStatus>('/api/overview/status'));
  }

  getElevation(e: number, n: number): Promise<number | null> {
    const params = new HttpParams().set('e', e.toFixed(1)).set('n', n.toFixed(1));
    return firstValueFrom(this.http.get<{ elevation: number | null }>('/api/elevation', { params })).then(
      (r) => r.elevation,
    );
  }

  getEstimate(p: EstimateParams): Promise<Estimate> {
    let params = new HttpParams()
      .set('minE', p.area.minE)
      .set('minN', p.area.minN)
      .set('maxE', p.area.maxE)
      .set('maxN', p.area.maxN)
      .set('mpb', p.metresPerBlock)
      .set('source', p.source)
      .set('res', p.alti3dResolution)
      .set('baseY', p.baseY);
    if (p.verticalScale != null) params = params.set('vscale', p.verticalScale);
    return firstValueFrom(this.http.get<Estimate>('/api/estimate', { params }));
  }

  createJob(body: JobRequest): Promise<JobDto> {
    return firstValueFrom(this.http.post<JobDto>('/api/jobs', body));
  }

  cancelJob(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/jobs/${id}`));
  }

  getJob(id: string): Promise<JobDto> {
    return firstValueFrom(this.http.get<JobDto>(`/api/jobs/${id}`));
  }

  revealJob(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/jobs/${id}/reveal`, {}));
  }

  getBlocks(): Promise<BlocksInfo> {
    return firstValueFrom(this.http.get<BlocksInfo>('/api/blocks'));
  }

  getRegion(e: number, n: number, level: RegionLevel): Promise<RegionInfo> {
    const params = new HttpParams().set('e', e.toFixed(1)).set('n', n.toFixed(1)).set('level', level);
    return firstValueFrom(this.http.get<RegionInfo>('/api/region', { params }));
  }

  getDatasets(): Promise<DatasetsStatus> {
    return firstValueFrom(this.http.get<DatasetsStatus>('/api/datasets'));
  }

  prepareDataset(kind: 'tlm3d' | 'regio'): Promise<DatasetStatus> {
    return firstValueFrom(this.http.post<DatasetStatus>(`/api/datasets/${kind}/prepare`, {}));
  }

  clearCache(): Promise<{ cacheBytes: number }> {
    return firstValueFrom(this.http.post<{ cacheBytes: number }>('/api/cache/clear', {}));
  }

  /** Server-sent job updates; completes when the job reaches a terminal state. */
  streamJob(id: string): Observable<JobDto> {
    return new Observable<JobDto>((subscriber) => {
      const source = new EventSource(`/api/jobs/${id}/events`);
      source.onmessage = (ev) => {
        const dto = JSON.parse(ev.data) as JobDto;
        subscriber.next(dto);
        if (dto.state === 'done' || dto.state === 'failed' || dto.state === 'cancelled') {
          source.close();
          subscriber.complete();
        }
      };
      source.onerror = () => {
        // The server closes the stream after the final event; treat a closed stream as completion
        // and let the caller re-fetch the job if it still wants the final state.
        if (source.readyState === EventSource.CLOSED) {
          subscriber.complete();
        } else {
          subscriber.error(new Error('Lost connection to the job event stream.'));
          source.close();
        }
      };
      return () => source.close();
    });
  }
}
