import { Component, inject, signal } from '@angular/core';
import { email, form, FormField as BoundField, required } from '@angular/forms/signals';
import { Api, problemMessage } from '../../core/api';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';

interface Assignment {
  roleName: string;
}
interface UserRow {
  id: string;
  email: string;
  displayName: string;
  status: string;
  assignments?: Assignment[];
}
interface RoleRow {
  id: string;
  name: string;
  isTemplate: boolean;
}

@Component({
  selector: 'app-people',
  imports: [BoundField, Button, FieldControl, Card, DataTable, FormField, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>People and roles</ng-container>
        <ng-container pageSubtitle i18n
          >Staff accounts and the roles they can be given</ng-container
        >
      </app-page-header>
      <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
        <div class="flex min-w-0 flex-1 flex-col gap-5">
          <app-data-table
            [columns]="userColumns"
            [rows]="users()"
            [track]="trackUser"
            empty="No staff yet."
          />
          <app-data-table
            [columns]="roleColumns"
            [rows]="roles()"
            [track]="trackRole"
            empty="No roles yet."
          />
        </div>
        <app-card class="w-full lg:w-96">
          <h2 i18n>Invite a colleague</h2>
          <form class="flex flex-col gap-3" (submit)="create($event)">
            <app-form-field label="Name" [required]="true">
              <input appControl [formField]="draft.displayName" />
            </app-form-field>
            <app-form-field label="Email" [required]="true">
              <input appControl type="email" [formField]="draft.email" />
            </app-form-field>
            <app-form-field label="Temporary password" [required]="true">
              <input appControl type="password" [formField]="draft.password" />
            </app-form-field>
            <button appButton i18n>Create account</button>
          </form>
        </app-card>
        <app-card class="w-full lg:w-96">
          <h2 i18n>Assign a role</h2>
          <form class="flex flex-col gap-3" (submit)="assign($event)">
            <app-form-field label="Person" [required]="true">
              <select appControl name="userId" required>
                <option value="" i18n>Select</option>
                @for (user of users(); track user.id) {
                  <option [value]="user.id">{{ user.displayName }}</option>
                }
              </select>
            </app-form-field>
            <app-form-field label="Role" [required]="true">
              <select appControl name="roleId" required>
                <option value="" i18n>Select</option>
                @for (role of roles(); track role.id) {
                  <option [value]="role.id">{{ role.name }}</option>
                }
              </select>
            </app-form-field>
            <button appButton variant="secondary" i18n>Assign role</button>
          </form>
        </app-card>
      </div>
      @if (notice()) {
        <p class="text-sm text-ink-soft" role="status">{{ notice() }}</p>
      }
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class PeoplePage {
  private readonly api = inject(Api);
  protected readonly users = signal<UserRow[]>([]);
  protected readonly roles = signal<RoleRow[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly model = signal({ displayName: '', email: '', password: '' });
  protected readonly userColumns: DataColumn<UserRow>[] = [
    {
      header: 'Name',
      value: (row) => row.displayName,
      detail: (row) =>
        [row.email, ...(row.assignments ?? []).map((item) => item.roleName)].filter(Boolean).join(' · '),
    },
    { header: 'Status', value: (row) => row.status, status: true },
  ];
  protected readonly roleColumns: DataColumn<RoleRow>[] = [
    { header: 'Role', value: (row) => row.name },
    { header: 'Kind', value: (row) => (row.isTemplate ? 'Template' : 'Custom'), status: true },
  ];
  protected readonly trackUser = (row: UserRow) => row.id;
  protected readonly trackRole = (row: RoleRow) => row.id;
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
    this.notice.set(null);
    if (this.draft().invalid() || this.model().password.length < 12) {
      this.error.set('Enter a name, a valid email, and a password of at least 12 characters with upper and lower case, a number, and a symbol.');
      return;
    }
    try {
      await this.api.send('POST', '/api/identity/v1/users', this.model(), true);
      this.model.set({ displayName: '', email: '', password: '' });
      this.notice.set('Account created. Assign a role so they can sign in to something.');
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async assign(event: Event): Promise<void> {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    const userId = data.get('userId')?.toString();
    const roleId = data.get('roleId')?.toString();
    this.error.set(null);
    this.notice.set(null);
    if (!userId || !roleId) {
      this.error.set('Choose a person and a role.');
      return;
    }
    try {
      await this.api.send(
        'POST',
        `/api/identity/v1/roles/${roleId}/assignments`,
        { userId, scope: 'All', hiringUnitIds: [], functionIds: [] },
        true,
      );
      (event.target as HTMLFormElement).reset();
      this.notice.set('Role assigned.');
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
