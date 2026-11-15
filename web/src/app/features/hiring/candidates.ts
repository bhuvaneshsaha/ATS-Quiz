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
        <ng-container pageSubtitle i18n>People shared across openings</ng-container>
      </app-page-header>
      <app-data-table
        [columns]="columns"
        [rows]="candidates()"
        [track]="track"
        [empty]="copy.emptyCandidates"
      />
      <app-card>
        <h2 i18n>Add a candidate</h2>
        <form class="grid gap-3 md:grid-cols-2" (submit)="create($event)">
          <app-form-field label="Name" [required]="true">
            <input appControl [formField]="draft.name" />
          </app-form-field>
          <app-form-field label="Email" [required]="true">
            <input appControl type="email" [formField]="draft.email" />
          </app-form-field>
          <app-form-field class="md:col-span-2" label="Add to opening">
            <select appControl name="opening">
              <option value="">Do not apply yet</option>
              @for (opening of openings(); track opening.id) {
                <option [value]="opening.id">{{ opening.title }}</option>
              }
            </select>
          </app-form-field>
          <label class="flex min-h-11 items-center gap-2 text-ink md:col-span-2">
            <input class="size-5 accent-accent" type="checkbox" name="consent" required />
            <span i18n>Consent to keep this record (required)</span>
          </label>
          <button appButton class="md:col-span-2" i18n>Save candidate</button>
          @if (error()) {
            <p class="text-sm text-danger md:col-span-2" role="alert">{{ error() }}</p>
          }
        </form>
      </app-card>
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
    { header: 'Name', value: (row) => row.name },
    { header: 'Email', value: (row) => row.email },
    { header: 'Location', value: (row) => row.location ?? '' },
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
