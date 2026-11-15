import { Component } from '@angular/core';

@Component({
  selector: 'app-card',
  template: `
    <section class="flex flex-col gap-4 rounded-card border border-line bg-surface p-5">
      <ng-content />
    </section>
  `,
})
export class Card {}
