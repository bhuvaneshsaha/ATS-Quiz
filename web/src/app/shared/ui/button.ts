import { Directive, input } from '@angular/core';
import { BrnButton } from '@spartan-ng/brain/button';

export type ButtonVariant = 'primary' | 'dark' | 'secondary' | 'dashed' | 'danger';

@Directive({
  selector: 'button[appButton], a[appButton]',
  hostDirectives: [BrnButton],
  host: {
    class: 'btn',
    '[attr.data-variant]': 'variant()',
  },
})
export class Button {
  readonly variant = input<ButtonVariant>('primary');
}
