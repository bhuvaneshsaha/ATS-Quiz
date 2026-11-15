import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-progress-bar',
  template: `
    <div
      class="h-2 overflow-hidden rounded-pill bg-line"
      role="progressbar"
      [attr.aria-valuenow]="value()"
      aria-valuemin="0"
      [attr.aria-valuemax]="max()"
    >
      <div class="h-full bg-accent" [style.width.%]="percent()"></div>
    </div>
  `,
})
export class ProgressBar {
  readonly value = input(0);
  readonly max = input(100);
  protected readonly percent = computed(() => {
    const max = this.max();
    if (max <= 0) {
      return 0;
    }
    return Math.max(0, Math.min(100, (this.value() / max) * 100));
  });
}
