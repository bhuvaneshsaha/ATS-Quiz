import { Component, inject, signal } from '@angular/core';
import { email, form, FormField as BoundField, required } from '@angular/forms/signals';
import { Api, problemMessage } from '../../core/api';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';

interface UserRow {
  id: string;
  email: string;
  displayName: string;
  status: string;
}
interface RoleRow {
  id: string;
  name: string;
  isTemplate: boolean;
}

@Component({
  selector: 'app-people',
  imports: [BoundField, Button, FieldControl, Card, FormField, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>People and roles</ng-container>
        <ng-container pageSubtitle i18n
          >Staff accounts and the roles they can be given</ng-container
        >
      </app-page-header>
      <div class="grid gap-5 md:grid-cols-2">
        <app-card>
          <h2 i18n>Staff</h2>
          <ul class="flex flex-col gap-2">
            @for (user of users(); track user.id) {
              <li class="rounded-control border border-line-soft px-3 py-2">
                {{ user.displayName }} · {{ user.email }}
              </li>
            }
          </ul>
        </app-card>
        <app-card>
          <h2 i18n>Roles</h2>
          <ul class="flex flex-col gap-2">
            @for (role of roles(); track role.id) {
              <li class="rounded-control border border-line-soft px-3 py-2">{{ role.name }}</li>
            }
          </ul>
        </app-card>
      </div>
      <app-card>
        <h2 i18n>Invite a colleague</h2>
        <form class="grid gap-3 md:grid-cols-2" (submit)="create($event)">
          <app-form-field label="Name" [required]="true">
            <input appControl [formField]="draft.displayName" />
          </app-form-field>
          <app-form-field label="Email" [required]="true">
            <input appControl type="email" [formField]="draft.email" />
          </app-form-field>
          <app-form-field class="md:col-span-2" label="Temporary password" [required]="true">
            <input appControl type="password" [formField]="draft.password" />
          </app-form-field>
          <button appButton class="md:col-span-2" i18n>Create account</button>
          @if (error()) {
            <p class="text-sm text-danger md:col-span-2" role="alert">{{ error() }}</p>
          }
        </form>
      </app-card>
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
