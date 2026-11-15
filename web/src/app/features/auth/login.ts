import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { email, form, FormField as BoundField, required } from '@angular/forms/signals';
import { SessionStore } from '../../core/session';
import { firstAllowed } from '../shell/home';
import { Card } from '../../shared/components/card';
import { FormField } from '../../shared/components/form-field';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';

@Component({
  selector: 'app-login',
  imports: [BoundField, Card, FormField, Button, FieldControl],
  template: `
    <main class="mx-auto flex min-h-dvh max-w-md flex-col justify-center gap-6 px-4 py-10">
      <div class="flex flex-col gap-1">
        <p class="font-display text-3xl font-semibold" i18n>Shortlist</p>
        <p class="text-muted" i18n>Sign in to the hiring workspace.</p>
      </div>
      <app-card>
        <form class="flex flex-col gap-4" (submit)="submit($event)">
          <app-form-field label="Email" [required]="true">
            <input appControl type="email" autocomplete="username" [formField]="login.email" />
          </app-form-field>
          <app-form-field label="Password" [required]="true">
            <input
              appControl
              type="password"
              autocomplete="current-password"
              [formField]="login.password"
            />
          </app-form-field>
          @if (formError() || session.error(); as message) {
            <p class="text-sm text-danger" role="alert">{{ message }}</p>
          }
          <button appButton class="w-full" type="submit" [disabled]="login().invalid()" i18n>
            Sign in
          </button>
        </form>
      </app-card>
    </main>
  `,
})
export class LoginPage {
  private readonly router = inject(Router);
  protected readonly session = inject(SessionStore);
  protected readonly formError = signal<string | null>(null);
  protected readonly model = signal({ email: '', password: '' });
  protected readonly login = form(this.model, (path) => {
    required(path.email);
    email(path.email);
    required(path.password);
  });

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.formError.set(null);
    if (this.login().invalid()) {
      this.formError.set('Enter a valid email and your password.');
      return;
    }

    const ok = await this.session.signIn(this.model().email, this.model().password);
    if (ok) {
      await this.router.navigateByUrl(firstAllowed((permission) => this.session.can(permission)) ?? '/');
    }
  }
}
