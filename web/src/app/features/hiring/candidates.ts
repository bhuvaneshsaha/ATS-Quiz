import { Component, inject, signal } from '@angular/core';
import { form, FormField, required, email } from '@angular/forms/signals';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';

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
  imports: [FormField],
  template: `
    <section class="mx-auto max-w-5xl px-4 py-6">
      <h1 class="font-serif text-3xl" i18n>Candidates</h1>
      <div class="mt-6 hidden md:block">
        <table class="w-full text-left text-sm">
          <thead><tr><th class="py-2" i18n>Name</th><th i18n>Email</th><th i18n>Location</th></tr></thead>
          <tbody>
            @for (candidate of candidates(); track candidate.id) {
              <tr class="border-t border-line">
                <td class="py-3">{{ candidate.name }}</td>
                <td>{{ candidate.email }}</td>
                <td>{{ candidate.location }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <div class="mt-4 grid gap-3 md:hidden">
        @for (candidate of candidates(); track candidate.id) {
          <article class="rounded-2xl border border-line bg-card p-4">
            <p class="font-medium">{{ candidate.name }}</p>
            <p class="text-sm">{{ candidate.email }}</p>
          </article>
        }
      </div>
      @if (candidates().length === 0) { <p class="mt-6 text-sm text-ink/70">{{ copy.emptyCandidates }}</p> }

      <form class="mt-8 grid gap-3 rounded-2xl border border-line bg-card p-4 md:grid-cols-2" (submit)="create($event)">
        <h2 class="font-serif text-xl md:col-span-2" i18n>Add a candidate</h2>
        <label class="text-sm" i18n>Name <input class="mt-1 h-11 w-full rounded-lg border border-line px-3" [formField]="draft.name" /></label>
        <label class="text-sm" i18n>Email <input class="mt-1 h-11 w-full rounded-lg border border-line px-3" type="email" [formField]="draft.email" /></label>
        <label class="text-sm md:col-span-2" i18n>Add to opening
          <select class="mt-1 h-11 w-full rounded-lg border border-line px-3" name="opening">
            <option value="">Do not apply yet</option>
            @for (opening of openings(); track opening.id) { <option [value]="opening.id">{{ opening.title }}</option> }
          </select>
        </label>
        <label class="flex items-center gap-2 text-sm md:col-span-2"><input type="checkbox" name="consent" required /> <span i18n>Consent to keep this record</span></label>
        <button class="h-11 rounded-lg bg-teal text-card md:col-span-2" i18n>Save candidate</button>
        @if (error()) { <p class="text-sm text-clay md:col-span-2" role="alert">{{ error() }}</p> }
      </form>
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
      const created = await this.api.send<{ id: string }>('POST', '/api/core/v1/candidates', {
        name: this.model().name,
        email: this.model().email,
        phone: null,
        location: null,
        source: 'Manual',
        externalId: null,
        consent: formData.get('consent') === 'on',
        consentText: 'Recorded in Shortlist.',
      }, true);
      const openingId = formData.get('opening')?.toString();
      if (openingId) {
        await this.api.send('POST', `/api/core/v1/openings/${openingId}/applications`, { candidateId: created.id }, true);
      }
      this.model.set({ name: '', email: '' });
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
