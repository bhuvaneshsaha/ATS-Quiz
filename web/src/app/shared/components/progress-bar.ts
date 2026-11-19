import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-progress-bar',
  template: `
    <div
      class="overflow-hidden rounded-pill"
      role="progressbar"
      [class.h-1]="size() === 'line'"
      [class.h-2]="size() === 'track'"
      [class.h-3]="size() === 'bar'"
      [class.bg-line]="track() === 'line'"
      [class.bg-line-soft]="track() === 'soft'"
      [attr.aria-valuenow]="value()"
      aria-valuemin="0"
      [attr.aria-valuemax]="max()"
      [attr.aria-label]="label()"
    >
      <div class="h-full bg-accent" [style.width.%]="percent()"></div>
    </div>
  `,
})
export class ProgressBar {
  readonly value = input(0);
  readonly max = input(100);
  readonly size = input<'line' | 'track' | 'bar'>('track');
  readonly track = input<'line' | 'soft'>('line');
  readonly label = input('Progress');
  protected readonly percent = computed(() => {
    const max = this.max();
    if (max <= 0) {
      return 0;
    }
    return Math.max(0, Math.min(100, (this.value() / max) * 100));
  });
}
