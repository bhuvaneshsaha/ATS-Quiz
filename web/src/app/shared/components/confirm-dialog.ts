import { Component, input, output } from '@angular/core';
import { BrnDialogImports } from '@spartan-ng/brain/dialog';
import { Button, ButtonVariant } from '../ui/button';

@Component({
  selector: 'app-confirm-dialog',
  imports: [BrnDialogImports, Button],
  template: `
    <div brnDialog>
      <button appButton brnDialogTrigger type="button" [variant]="triggerVariant()">
        {{ triggerLabel() }}
      </button>
      <div brnDialogOverlay class="bg-ink/40"></div>
      <ng-template brnDialogContent>
        <div
          class="m-4 flex w-full max-w-md flex-col gap-4 rounded-card border border-line bg-surface p-5"
          role="document"
        >
          <h2 brnDialogTitle>{{ title() }}</h2>
          <p brnDialogDescription class="text-ink-soft">{{ body() }}</p>
          <div class="flex flex-wrap justify-end gap-2">
            <button appButton brnDialogClose variant="secondary" type="button">
              {{ cancelLabel() }}
            </button>
            <button
              appButton
              brnDialogClose
              [variant]="confirmVariant()"
              type="button"
              (click)="confirmed.emit()"
            >
              {{ confirmLabel() }}
            </button>
          </div>
        </div>
      </ng-template>
    </div>
  `,
})
export class ConfirmDialog {
  readonly title = input.required<string>();
  readonly body = input.required<string>();
  readonly triggerLabel = input.required<string>();
  readonly confirmLabel = input('Confirm');
  readonly cancelLabel = input('Cancel');
  readonly triggerVariant = input<ButtonVariant>('secondary');
  readonly confirmVariant = input<ButtonVariant>('dark');
  readonly confirmed = output<void>();
}
