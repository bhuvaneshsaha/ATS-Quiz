import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { form, FormField, required } from '@angular/forms/signals';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { setAllEntities, withEntities } from '@ngrx/signals/entities';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tapResponse } from '@ngrx/operators';
import { debounceTime, from, pipe, switchMap, tap } from 'rxjs';
import { signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';

export interface OpeningSummary {
  id: string;
  code: string;
  title: string;
  status: string;
  hiringUnitId: string;
  hiringUnitName: string;
  functionId: string;
  functionName: string;
  positions: number;
  applicationCount: number;
}

interface OpeningFilter {
  status: string;
  search: string;
}

export function setPending() {
  return { status: 'pending' as const, error: null };
}

export function setFulfilled() {
  return { status: 'fulfilled' as const, error: null };
}

export function setError(error: string) {
  return { status: 'error' as const, error };
}

export const OpeningsStore = signalStore(
  withState({ filter: { status: '', search: '' } as OpeningFilter, status: 'idle' as 'idle' | 'pending' | 'fulfilled' | 'error', error: null as string | null }),
  withEntities<OpeningSummary>(),
  withComputed(({ entities }) => ({
    count: computed(() => entities().length),
  })),
  withMethods((store, api = inject(Api)) => ({
    setFilter(patch: Partial<OpeningFilter>): void {
      patchState(store, (state) => ({ filter: { ...state.filter, ...patch } }));
    },
    load: rxMethod<OpeningFilter>(
      pipe(
        debounceTime(250),
        tap(() => patchState(store, setPending())),
        switchMap((filter) =>
          from(
            api.get<{ items: OpeningSummary[] }>(
              `/api/core/v1/openings?limit=50&status=${encodeURIComponent(filter.status)}&search=${encodeURIComponent(filter.search)}`,
            ),
          ).pipe(
            tapResponse({
              next: (page) => patchState(store, setAllEntities(page.items), setFulfilled()),
              error: (error: HttpErrorResponse) => patchState(store, setError(problemMessage(error))),
            }),
          ),
        ),
      ),
    ),
  })),
);

@Component({
  selector: 'app-openings',
  imports: [RouterLink, FormField],
  providers: [OpeningsStore],
  template: `
    <section class="mx-auto max-w-5xl px-4 py-6">
      <header class="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <div>
          <h1 class="font-serif text-3xl" i18n>Openings</h1>
          <p class="text-sm text-ink/70" i18n>{{ store.count() }} shown</p>
        </div>
        <div class="flex gap-2">
          <input class="h-11 rounded-lg border border-line bg-card px-3" [value]="store.filter().search" (input)="store.setFilter({ search: $any($event.target).value })" i18n-placeholder placeholder="Search" />
          <select class="h-11 rounded-lg border border-line bg-card px-3" (change)="store.setFilter({ status: $any($event.target).value })">
            <option value="" i18n>Any status</option>
            <option value="Draft">Draft</option>
            <option value="Open">Open</option>
            <option value="OnHold">On hold</option>
          </select>
        </div>
      </header>

      @if (store.error(); as message) {
        <p class="mt-4 text-sm text-clay" role="alert">{{ message }}</p>
      }

      <div class="mt-6 hidden md:block">
        <table class="w-full text-left text-sm">
          <thead class="text-ink/60">
            <tr><th class="py-2" i18n>Code</th><th i18n>Title</th><th i18n>Unit</th><th i18n>Status</th></tr>
          </thead>
          <tbody>
            @for (opening of store.entities(); track opening.id) {
              <tr class="border-t border-line">
                <td class="py-3">{{ opening.code }}</td>
                <td><a class="underline" [routerLink]="['/openings', opening.id]">{{ opening.title }}</a></td>
                <td>{{ opening.hiringUnitName }}</td>
                <td><span class="rounded-full bg-card px-2 py-1">{{ opening.status }}</span></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <div class="mt-6 grid gap-3 md:hidden">
        @for (opening of store.entities(); track opening.id) {
          <a class="rounded-2xl border border-line bg-card p-4" [routerLink]="['/openings', opening.id]">
            <p class="text-xs text-ink/60">{{ opening.code }}</p>
            <p class="font-medium">{{ opening.title }}</p>
            <p class="text-sm">{{ opening.hiringUnitName }} · {{ opening.status }}</p>
          </a>
        }
      </div>
      @if (store.status() === 'fulfilled' && store.count() === 0) {
        <p class="mt-8 text-sm text-ink/70">{{ copy.emptyOpenings }}</p>
      }

      <form class="mt-8 grid gap-3 rounded-2xl border border-line bg-card p-4 md:grid-cols-2" (submit)="create($event)">
        <h2 class="font-serif text-xl md:col-span-2" i18n>New opening</h2>
        <label class="text-sm" i18n>Title <input class="mt-1 h-11 w-full rounded-lg border border-line px-3" [formField]="draft.title" /></label>
        <label class="text-sm" i18n>Positions <input class="mt-1 h-11 w-full rounded-lg border border-line px-3" type="number" [formField]="draft.positions" /></label>
        <label class="text-sm" i18n>Hiring unit
          <select class="mt-1 h-11 w-full rounded-lg border border-line px-3" [formField]="draft.hiringUnitId">
            <option value="">Select</option>
            @for (unit of units(); track unit.id) { <option [value]="unit.id">{{ unit.name }}</option> }
          </select>
        </label>
        <label class="text-sm" i18n>Function
          <select class="mt-1 h-11 w-full rounded-lg border border-line px-3" [formField]="draft.functionId">
            <option value="">Select</option>
            @for (item of functions(); track item.id) { <option [value]="item.id">{{ item.name }}</option> }
          </select>
        </label>
        <button class="h-11 rounded-lg bg-teal text-card md:col-span-2 disabled:opacity-50" [disabled]="draft().invalid()" i18n>Create opening</button>
        @if (formError()) { <p class="text-sm text-clay md:col-span-2" role="alert">{{ formError() }}</p> }
      </form>
    </section>
  `,
})
export class OpeningsPage {
  protected readonly store = inject(OpeningsStore);
  protected readonly copy = en;
  private readonly api = inject(Api);
  protected readonly units = signal<{ id: string; name: string }[]>([]);
  protected readonly functions = signal<{ id: string; name: string }[]>([]);
  protected readonly formError = signal<string | null>(null);
  protected readonly model = signal({ title: '', positions: 1, hiringUnitId: '', functionId: '' });
  protected readonly draft = form(this.model, (path) => {
    required(path.title);
    required(path.hiringUnitId);
    required(path.functionId);
  });

  constructor() {
    void this.loadChoices();
    this.store.load(this.store.filter);
  }

  private async loadChoices(): Promise<void> {
    const [units, functions] = await Promise.all([
      this.api.get<{ id: string; name: string }[]>('/api/core/v1/hiring-units'),
      this.api.get<{ id: string; name: string }[]>('/api/core/v1/functions'),
    ]);
    this.units.set(units);
    this.functions.set(functions);
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    this.formError.set(null);
    const value = this.model();
    try {
      await this.api.send('POST', '/api/core/v1/openings', {
        title: value.title,
        hiringUnitId: value.hiringUnitId,
        functionId: value.functionId,
        positions: Number(value.positions) || 1,
        location: null,
        seniority: null,
        reason: null,
        targetDate: null,
        hiringManagerId: null,
        recruiterId: null,
        externalId: null,
      }, true);
      this.model.set({ title: '', positions: 1, hiringUnitId: '', functionId: '' });
      this.store.load(this.store.filter());
    } catch (error) {
      this.formError.set(problemMessage(error));
    }
  }
}
