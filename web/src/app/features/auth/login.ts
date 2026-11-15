import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { form, FormField, required, email } from '@angular/forms/signals';
import { SessionStore } from '../../core/session';

@Component({
  selector: 'app-login',
  imports: [FormField],
  template: `
    <main class="mx-auto flex min-h-dvh max-w-md flex-col justify-center px-5 py-10">
      <p class="font-serif text-3xl" i18n>Shortlist</p>
      <p class="mt-2 text-sm text-ink/70" i18n>Sign in to the hiring workspace.</p>
      <form class="mt-8 space-y-4 rounded-2xl border border-line bg-card p-6" (submit)="submit($event)">
        <label class="block text-sm" i18n>
          Email
          <input class="mt-1 h-11 w-full rounded-lg border border-line bg-paper px-3" type="email" [formField]="login.email" />
        </label>
        <label class="block text-sm" i18n>
          Password
          <input class="mt-1 h-11 w-full rounded-lg border border-line bg-paper px-3" type="password" [formField]="login.password" />
        </label>
        @if (session.error(); as message) {
          <p class="text-sm text-clay" role="alert">{{ message }}</p>
        }
        <button class="h-11 w-full rounded-lg bg-teal text-card disabled:opacity-50" type="submit" [disabled]="login().invalid()" i18n>
          Sign in
        </button>
      </form>
    </main>
  `,
})
export class LoginPage {
  private readonly router = inject(Router);
  protected readonly session = inject(SessionStore);
  protected readonly model = signal({ email: '', password: '' });
  protected readonly login = form(this.model, (path) => {
    required(path.email);
    email(path.email);
    required(path.password);
  });

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.login().invalid()) {
      return;
    }

    const ok = await this.session.signIn(this.model().email, this.model().password);
    if (ok) {
      await this.router.navigateByUrl('/openings');
    }
  }
}
