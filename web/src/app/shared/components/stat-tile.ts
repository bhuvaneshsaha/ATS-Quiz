import { Component, input } from '@angular/core';

@Component({
  selector: 'app-stat-tile',
  template: `
    <article class="flex flex-col gap-1 rounded-card border border-line bg-surface px-4 py-3.5">
      <p class="text-sm text-muted">{{ label() }}</p>
      <p class="font-display text-2xl font-semibold tabular-nums">{{ value() }}</p>
      @if (context()) {
        <p class="text-sm text-ink-soft">{{ context() }}</p>
      }
    </article>
  `,
})
export class StatTile {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly context = input<string | null>(null);
}
