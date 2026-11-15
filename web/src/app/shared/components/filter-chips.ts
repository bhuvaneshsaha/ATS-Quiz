import { Component, input, output } from '@angular/core';
import { BrnToggleGroupImports } from '@spartan-ng/brain/toggle-group';

export interface FilterChip {
  value: string;
  label: string;
  count?: number;
}

@Component({
  selector: 'app-filter-chips',
  imports: [BrnToggleGroupImports],
  template: `
    <div
      class="flex flex-wrap gap-2"
      brnToggleGroup
      type="single"
      [nullable]="false"
      [value]="value()"
      (valueChange)="pick($event)"
    >
      @for (chip of chips(); track chip.value) {
        <button
          brnToggleGroupItem
          type="button"
          [value]="chip.value"
          class="min-h-11 rounded-pill border px-3.5 font-medium transition-colors duration-150"
          [class.border-ink]="value() === chip.value"
          [class.bg-ink]="value() === chip.value"
          [class.text-white]="value() === chip.value"
          [class.border-line-strong]="value() !== chip.value"
          [class.bg-surface]="value() !== chip.value"
          [class.text-ink]="value() !== chip.value"
        >
          {{ chip.label }}
          @if (chip.count !== undefined) {
            <span class="font-normal"> {{ chip.count }}</span>
          }
        </button>
      }
    </div>
  `,
})
export class FilterChips {
  readonly chips = input.required<FilterChip[]>();
  readonly value = input('');
  readonly valueChange = output<string>();

  protected pick(value: string | string[] | null | undefined): void {
    if (typeof value === 'string') {
      this.valueChange.emit(value);
    }
  }
}
