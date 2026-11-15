import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { StatusPill } from '../../shared/components/status-pill';

interface Named {
  id: string;
  name: string;
  type?: string;
}

@Component({
  selector: 'app-workspace',
  imports: [Button, FieldControl, Card, FormField, PageHeader, StatusPill],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Workspace</ng-container>
        <ng-container pageSubtitle i18n>Hiring units and functions for this company</ng-container>
      </app-page-header>
      <div class="grid gap-5 md:grid-cols-2">
        <app-card>
          <h2 i18n>Hiring units</h2>
          <ul class="flex flex-col gap-2">
            @for (unit of units(); track unit.id) {
              <li
                class="flex items-center justify-between gap-3 rounded-control border border-line-soft px-3 py-2"
              >
                <span>{{ unit.name }}</span>
                <app-status-pill [status]="unit.type ?? 'Internal'" />
              </li>
            }
          </ul>
          <form class="flex flex-col gap-3" (submit)="addUnit($event)">
            <app-form-field label="Unit name" [required]="true">
              <input appControl name="name" required />
            </app-form-field>
            <app-form-field label="Type" [required]="true">
              <select appControl name="type">
                <option>Department</option>
                <option>Client</option>
              </select>
            </app-form-field>
            <button appButton i18n>Add hiring unit</button>
          </form>
        </app-card>
        <app-card>
          <h2 i18n>Functions</h2>
          <ul class="flex flex-col gap-2">
            @for (item of functions(); track item.id) {
              <li class="rounded-control border border-line-soft px-3 py-2">{{ item.name }}</li>
            }
          </ul>
          <form class="flex flex-col gap-3" (submit)="addFunction($event)">
            <app-form-field label="Function name" [required]="true">
              <input appControl name="name" required />
            </app-form-field>
            <button appButton i18n>Add function</button>
          </form>
        </app-card>
      </div>
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class WorkspacePage {
  private readonly api = inject(Api);
  protected readonly units = signal<Named[]>([]);
  protected readonly functions = signal<Named[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.reload();
  }

  private async reload(): Promise<void> {
    const [units, functions] = await Promise.all([
      this.api.get<Named[]>('/api/core/v1/hiring-units'),
      this.api.get<Named[]>('/api/core/v1/functions'),
    ]);
    this.units.set(units);
    this.functions.set(functions);
  }

  protected async addUnit(event: Event): Promise<void> {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    await this.save('/api/core/v1/hiring-units', {
      type: data.get('type'),
      name: data.get('name'),
      ownerUserId: null,
      externalId: null,
    });
    (event.target as HTMLFormElement).reset();
  }

  protected async addFunction(event: Event): Promise<void> {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    await this.save('/api/core/v1/functions', { name: data.get('name'), ownerUserId: null });
    (event.target as HTMLFormElement).reset();
  }

  private async save(url: string, body: unknown): Promise<void> {
    this.error.set(null);
    try {
      await this.api.send('POST', url, body, true);
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
