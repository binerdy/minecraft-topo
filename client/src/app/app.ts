import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MapComponent } from './map/map.component';
import { PanelComponent } from './panel/panel.component';
import { AppState } from './state/app-state.service';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, MapComponent, PanelComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly state = inject(AppState);
}
