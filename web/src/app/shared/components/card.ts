import { Component, input } from '@angular/core';

@Component({
  selector: 'app-card',
  template: `
    <section
      class="flex flex-col gap-4 border border-line bg-surface"
      [class.rounded-card]="tone() === 'card'"
      [class.p-5]="tone() === 'card'"
      [class.rounded-panel]="tone() === 'panel'"
      [class.p-7]="tone() === 'panel'"
    >
      <ng-content />
    </section>
  `,
})
export class Card {
  readonly tone = input<'card' | 'panel'>('card');
}
