import { Directive } from '@angular/core';

@Directive({
  selector: 'input[appControl], select[appControl], textarea[appControl]',
  host: { class: 'field-control' },
})
export class FieldControl {}
