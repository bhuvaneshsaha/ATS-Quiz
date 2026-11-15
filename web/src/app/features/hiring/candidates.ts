import { Component, inject, signal } from '@angular/core';
import { email, form, FormField as BoundField, required } from '@angular/forms/signals';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';

interface Candidate {
  id: string;
  name: string;
  email: string;
  location: string | null;
  source: string | null;
}

interface Opening {
  id: string;
  title: string;
}

@Component({
  selector: 'app-candidates',
  imports: [BoundField, Button, FieldControl, Card, DataTable, FormField, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Candidates</ng-container>
        <ng-container pageSubtitle i18n
          >{{ candidates().length }} people · status is kept on each opening</ng-container
        >
      </app-page-header>
      <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
        <app-data-table
          class="min-w-0 flex-1"
          [columns]="columns"
          [rows]="candidates()"
          [track]="track"
          [empty]="copy.emptyCandidates"
        />
        <app-card class="w-full lg:w-80">
          <h2 i18n>Add a candidate</h2>
          <form class="flex flex-col gap-3" (submit)="create($event)">
            <app-form-field label="Name" [required]="true">
              <input appControl [formField]="draft.name" />
            </app-form-field>
            <app-form-field label="Email" [required]="true">
              <input appControl type="email" [formField]="draft.email" />
            </app-form-field>
            <app-form-field label="Opening">
              <select appControl name="opening">
                <option value="">Do not apply yet</option>
                @for (opening of openings(); track opening.id) {
                  <option [value]="opening.id">{{ opening.title }}</option>
                }
              </select>
            </app-form-field>
            <label class="flex min-h-11 items-center gap-2 text-ink">
              <input class="size-5 accent-accent" type="checkbox" name="consent" required />
              <span i18n>Consent to keep this record (required)</span>
            </label>
            <button appButton i18n>Save candidate</button>
            @if (error()) {
              <p class="text-sm text-danger" role="alert">{{ error() }}</p>
            }
          </form>
          <p class="text-sm text-muted" i18n>
            Each person is shared across openings. Applying them adds the first stage.
          </p>
        </app-card>
      </div>
    </section>
  `,
})
export class CandidatesPage {
  private readonly api = inject(Api);
  protected readonly copy = en;
  protected readonly candidates = signal<Candidate[]>([]);
  protected readonly openings = signal<Opening[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly model = signal({ name: '', email: '' });
  protected readonly draft = form(this.model, (path) => {
    required(path.name);
    required(path.email);
    email(path.email);
  });
  protected readonly columns: DataColumn<Candidate>[] = [
    { header: 'Candidate', value: (row) => row.name, detail: (row) => row.email },
    { header: 'Location', value: (row) => row.location ?? '' },
    { header: 'Source', value: (row) => row.source ?? 'Manual', status: true },
  ];
  protected readonly track = (row: Candidate) => row.id;

  constructor() {
    void this.reload();
  }

  private async reload(): Promise<void> {
    const [candidates, openings] = await Promise.all([
      this.api.get<{ items: Candidate[] }>('/api/core/v1/candidates?limit=50'),
      this.api.get<{ items: Opening[] }>('/api/core/v1/openings?limit=50'),
    ]);
    this.candidates.set(candidates.items);
    this.openings.set(openings.items);
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    const formData = new FormData(event.target as HTMLFormElement);
    this.error.set(null);
    try {
      const created = await this.api.send<{ id: string }>(
        'POST',
        '/api/core/v1/candidates',
        {
          name: this.model().name,
          email: this.model().email,
          phone: null,
          location: null,
          source: 'Manual',
          externalId: null,
          consent: formData.get('consent') === 'on',
          consentText: 'Recorded in Shortlist.',
        },
        true,
      );
      const openingId = formData.get('opening')?.toString();
      if (openingId) {
        await this.api.send(
          'POST',
          `/api/core/v1/openings/${openingId}/applications`,
          { candidateId: created.id },
          true,
        );
      }
      this.model.set({ name: '', email: '' });
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
