import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';

interface Named {
  id: string;
  name: string;
  type?: string;
}

@Component({
  selector: 'app-workspace',
  imports: [Button, FieldControl, Card, DataTable, FormField, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Departments and clients</ng-container>
        <ng-container pageSubtitle i18n
          >Who you hire for. A function owns its questions, whoever the opening is
          for.</ng-container
        >
      </app-page-header>
      <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
        <div class="flex min-w-0 flex-1 flex-col gap-5">
          <app-data-table
            [columns]="unitColumns"
            [rows]="units()"
            [track]="track"
            empty="No hiring units yet."
          />
          <app-data-table
            [columns]="functionColumns"
            [rows]="functions()"
            [track]="track"
            empty="No functions yet."
          />
        </div>
        <div class="flex w-full flex-col gap-5 lg:w-80">
          <app-card>
            <h2 i18n>Add a hiring unit</h2>
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
              <button appButton i18n>Add department or client</button>
            </form>
          </app-card>
          <app-card>
            <h2 i18n>Add a function</h2>
            <form class="flex flex-col gap-3" (submit)="addFunction($event)">
              <app-form-field label="Function name" [required]="true">
                <input appControl name="name" required />
              </app-form-field>
              <button appButton i18n>Add function</button>
            </form>
          </app-card>
        </div>
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
  protected readonly unitColumns: DataColumn<Named>[] = [
    { header: 'Name', value: (row) => row.name },
    { header: 'Type', value: (row) => row.type ?? 'Internal', status: true },
  ];
  protected readonly functionColumns: DataColumn<Named>[] = [
    { header: 'Function', value: (row) => row.name },
  ];
  protected readonly track = (row: Named) => row.id;

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
