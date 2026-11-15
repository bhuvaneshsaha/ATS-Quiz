import { Component, input } from '@angular/core';

@Component({
  selector: 'app-form-field',
  template: `
    <label class="flex flex-col gap-1 text-sm text-muted">
      <span>
        {{ label() }}
        @if (required()) {
          (required)
        }
      </span>
      <ng-content />
      @if (error(); as message) {
        <span class="text-danger" role="alert">{{ message }}</span>
      }
    </label>
  `,
})
export class FormField {
  readonly label = input.required<string>();
  readonly required = input(false);
  readonly error = input<string | null>(null);
}
