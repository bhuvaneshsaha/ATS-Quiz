import { Component, computed, effect, ElementRef, HostListener, inject, input, output, signal } from '@angular/core';

export interface ComboOption {
  value: string;
  label: string;
  hint?: string;
}

export function filterOptions(options: readonly ComboOption[], query: string): ComboOption[] {
  const text = query.trim().toLowerCase();
  if (!text) {
    return [...options];
  }
  return options.filter(
    (option) =>
      option.label.toLowerCase().includes(text) || (option.hint ?? '').toLowerCase().includes(text),
  );
}

let nextId = 0;

@Component({
  selector: 'app-combobox',
  template: `
    <div class="relative">
      <input type="hidden" [name]="name()" [value]="selected()" />
      <input
        class="field-control w-full"
        role="combobox"
        type="text"
        autocomplete="off"
        [attr.aria-expanded]="open()"
        [attr.aria-controls]="listId"
        [attr.aria-activedescendant]="activeId()"
        aria-autocomplete="list"
        [attr.aria-required]="required() || null"
        [attr.aria-invalid]="invalid() || null"
        [attr.aria-describedby]="describedBy() || null"
        [placeholder]="placeholder()"
        [value]="query()"
        (focus)="openList()"
        (input)="onType($any($event.target).value)"
        (keydown)="onKey($event)"
      />
      @if (open()) {
        <ul
          class="absolute z-20 mt-1 max-h-60 w-full overflow-auto rounded-control border border-line bg-surface p-1 shadow-none"
          role="listbox"
          [id]="listId"
        >
          @for (option of visible(); track option.value; let index = $index) {
            <li
              role="option"
              class="cursor-pointer rounded-control px-3 py-2"
              [id]="optionId(index)"
              [attr.aria-selected]="option.value === value()"
              [class.bg-accent-tint]="index === active()"
              (mousedown)="choose(option, $event)"
            >
              <span class="block">{{ option.label }}</span>
              @if (option.hint) {
                <span class="block text-sm text-muted">{{ option.hint }}</span>
              }
            </li>
          } @empty {
            <li class="px-3 py-2 text-sm text-muted" role="presentation">No matches</li>
          }
        </ul>
      }
    </div>
  `,
})
export class Combobox {
  readonly options = input<readonly ComboOption[]>([]);
  readonly value = input('');
  readonly name = input('');
  readonly placeholder = input('Type to search');
  readonly required = input(false);
  readonly invalid = input(false);
  readonly describedBy = input('');
  readonly valueChange = output<string>();

  private readonly host = inject(ElementRef<HTMLElement>);
  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly active = signal(0);
  private readonly selected = signal('');
  private readonly filtering = signal(false);
  protected readonly listId = `combo-list-${++nextId}`;
  protected readonly visible = computed(() =>
    this.filtering() ? filterOptions(this.options(), this.query()) : [...this.options()],
  );

  constructor() {
    effect(() => {
      const value = this.value();
      this.selected.set(value);
      if (!this.open()) {
        this.query.set(this.options().find((option) => option.value === value)?.label ?? '');
      }
    });
  }

  protected activeId(): string | null {
    return this.visible()[this.active()] ? this.optionId(this.active()) : null;
  }

  protected optionId(index: number): string {
    return `${this.listId}-opt-${index}`;
  }

  protected openList(): void {
    this.filtering.set(false);
    this.open.set(true);
    this.active.set(0);
  }

  protected onType(value: string): void {
    this.query.set(value);
    this.filtering.set(true);
    this.open.set(true);
    this.active.set(0);
    if (!value.trim()) {
      this.valueChange.emit('');
    }
  }

  protected onKey(event: KeyboardEvent): void {
    const items = this.visible();
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.open.set(true);
      if (!items.length) return;
      this.active.update((index) => Math.min(items.length - 1, index + 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.active.update((index) => Math.max(0, index - 1));
    } else if (event.key === 'Enter' && this.open() && items[this.active()]) {
      event.preventDefault();
      this.choose(items[this.active()]);
    } else if (event.key === 'Escape') {
      this.open.set(false);
    }
  }

  protected choose(option: ComboOption, event?: Event): void {
    event?.preventDefault();
    this.selected.set(option.value);
    this.query.set(option.label);
    this.filtering.set(false);
    this.open.set(false);
    this.valueChange.emit(option.value);
  }

  @HostListener('focusout', ['$event'])
  protected closeLater(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (next instanceof Node && this.host.nativeElement.contains(next)) {
      return;
    }
    this.open.set(false);
    this.filtering.set(false);
    this.query.set(this.options().find((option) => option.value === this.selected())?.label ?? '');
  }
}
