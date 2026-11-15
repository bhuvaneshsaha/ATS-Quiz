import { Component, computed, inject, signal } from '@angular/core';
import { email, form, FormField as BoundField, required } from '@angular/forms/signals';
import { Api, problemMessage } from '../../core/api';
import { Button } from '../../shared/ui/button';
import { Combobox, ComboOption } from '../../shared/ui/combobox';
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
  imports: [BoundField, Button, Combobox, FieldControl, Card, DataTable, FormField, PageHeader],
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
            <app-form-field label="Temporary password" [required]="true" [error]="passwordError()" errorId="password-error">
              <input
                id="people-password"
                appControl
                type="password"
                [formField]="draft.password"
                [attr.aria-invalid]="passwordError() ? true : null"
                [attr.aria-describedby]="passwordError() ? 'password-error' : null"
              />
            </app-form-field>
            <button appButton i18n>Create account</button>
          </form>
        </app-card>
        <app-card class="w-full lg:w-96">
          <h2 i18n>Assign a role</h2>
          <form class="flex flex-col gap-3" (submit)="assign($event)">
            <app-form-field label="Person" [required]="true">
              <app-combobox
                name="userId"
                [options]="userOptions()"
                [value]="userId()"
                placeholder="Type a name"
                (valueChange)="userId.set($event)"
              />
            </app-form-field>
            <app-form-field label="Role" [required]="true">
              <app-combobox
                name="roleId"
                [options]="roleOptions()"
                [value]="roleId()"
                placeholder="Type a role"
                (valueChange)="roleId.set($event)"
              />
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
  protected readonly passwordError = signal<string | null>(null);
  protected readonly userId = signal('');
  protected readonly roleId = signal('');
  protected readonly userOptions = computed<ComboOption[]>(() =>
    this.users().map((user) => ({ value: user.id, label: user.displayName, hint: user.email })),
  );
  protected readonly roleOptions = computed<ComboOption[]>(() =>
    this.roles().map((role) => ({ value: role.id, label: role.name })),
  );
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
    this.passwordError.set(null);
    if (this.model().password.length < 12) {
      this.passwordError.set('Use at least 12 characters with upper and lower case, a number, and a symbol.');
      queueMicrotask(() => document.getElementById('people-password')?.focus());
      return;
    }
    if (this.draft().invalid()) {
      this.error.set('Enter a name and a valid email.');
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
    const userId = this.userId();
    const roleId = this.roleId();
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
      this.userId.set('');
      this.roleId.set('');
      this.notice.set('Role assigned.');
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
