import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';

interface Named {
  id: string;
  name: string;
  type?: string;
}

@Component({
  selector: 'app-workspace',
  template: `
    <section class="mx-auto max-w-5xl px-4 py-6">
      <h1 class="font-serif text-3xl" i18n>Workspace</h1>
      <div class="mt-6 grid gap-6 md:grid-cols-2">
        <div>
          <h2 class="font-serif text-xl" i18n>Hiring units</h2>
          <ul class="mt-3 space-y-2 text-sm">
            @for (unit of units(); track unit.id) { <li class="rounded-xl border border-line bg-card px-3 py-2">{{ unit.name }} · {{ unit.type }}</li> }
          </ul>
          <form class="mt-4 space-y-2" (submit)="addUnit($event)">
            <input class="h-11 w-full rounded-lg border border-line px-3" name="name" required i18n-placeholder placeholder="Unit name" />
            <select class="h-11 w-full rounded-lg border border-line px-3" name="type"><option>Department</option><option>Client</option></select>
            <button class="h-11 w-full rounded-lg bg-teal text-card" i18n>Add hiring unit</button>
          </form>
        </div>
        <div>
          <h2 class="font-serif text-xl" i18n>Functions</h2>
          <ul class="mt-3 space-y-2 text-sm">
            @for (item of functions(); track item.id) { <li class="rounded-xl border border-line bg-card px-3 py-2">{{ item.name }}</li> }
          </ul>
          <form class="mt-4 space-y-2" (submit)="addFunction($event)">
            <input class="h-11 w-full rounded-lg border border-line px-3" name="name" required i18n-placeholder placeholder="Function name" />
            <button class="h-11 w-full rounded-lg bg-ink text-card" i18n>Add function</button>
          </form>
        </div>
      </div>
      @if (error()) { <p class="mt-4 text-sm text-clay" role="alert">{{ error() }}</p> }
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
    await this.save('/api/core/v1/hiring-units', { type: data.get('type'), name: data.get('name'), ownerUserId: null, externalId: null });
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
