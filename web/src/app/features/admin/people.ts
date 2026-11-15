import { Component, inject, signal } from '@angular/core';
import { form, FormField, required, email } from '@angular/forms/signals';
import { Api, problemMessage } from '../../core/api';

interface UserRow { id: string; email: string; displayName: string; status: string }
interface RoleRow { id: string; name: string; isTemplate: boolean }

@Component({
  selector: 'app-people',
  imports: [FormField],
  template: `
    <section class="mx-auto max-w-5xl px-4 py-6">
      <h1 class="font-serif text-3xl" i18n>People and roles</h1>
      <div class="mt-6 grid gap-6 md:grid-cols-2">
        <div>
          <h2 class="font-serif text-xl" i18n>Staff</h2>
          <ul class="mt-3 space-y-2 text-sm">
            @for (user of users(); track user.id) { <li class="rounded-xl border border-line bg-card px-3 py-2">{{ user.displayName }} · {{ user.email }}</li> }
          </ul>
        </div>
        <div>
          <h2 class="font-serif text-xl" i18n>Roles</h2>
          <ul class="mt-3 space-y-2 text-sm">
            @for (role of roles(); track role.id) { <li class="rounded-xl border border-line bg-card px-3 py-2">{{ role.name }}</li> }
          </ul>
        </div>
      </div>
      <form class="mt-8 grid gap-3 rounded-2xl border border-line bg-card p-4 md:grid-cols-2" (submit)="create($event)">
        <h2 class="font-serif text-xl md:col-span-2" i18n>Invite a colleague</h2>
        <label class="text-sm" i18n>Name <input class="mt-1 h-11 w-full rounded-lg border border-line px-3" [formField]="draft.displayName" /></label>
        <label class="text-sm" i18n>Email <input class="mt-1 h-11 w-full rounded-lg border border-line px-3" type="email" [formField]="draft.email" /></label>
        <label class="text-sm md:col-span-2" i18n>Temporary password <input class="mt-1 h-11 w-full rounded-lg border border-line px-3" type="password" [formField]="draft.password" /></label>
        <button class="h-11 rounded-lg bg-teal text-card md:col-span-2" i18n>Create account</button>
        @if (error()) { <p class="text-sm text-clay md:col-span-2" role="alert">{{ error() }}</p> }
      </form>
    </section>
  `,
})
export class PeoplePage {
  private readonly api = inject(Api);
  protected readonly users = signal<UserRow[]>([]);
  protected readonly roles = signal<RoleRow[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly model = signal({ displayName: '', email: '', password: '' });
  protected readonly draft = form(this.model, (path) => {
    required(path.displayName);
    required(path.email);
    email(path.email);
    required(path.password);
  });

  constructor() {
    void this.reload();
  }

  private async reload(): Promise<void> {
    const [users, roles] = await Promise.all([
      this.api.get<UserRow[]>('/api/identity/v1/users'),
      this.api.get<RoleRow[]>('/api/identity/v1/roles'),
    ]);
    this.users.set(users);
    this.roles.set(roles);
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set(null);
    try {
      await this.api.send('POST', '/api/identity/v1/users', this.model(), true);
      this.model.set({ displayName: '', email: '', password: '' });
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
