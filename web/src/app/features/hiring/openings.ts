import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { form, FormField as BoundField, required } from '@angular/forms/signals';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { setAllEntities, withEntities } from '@ngrx/signals/entities';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tapResponse } from '@ngrx/operators';
import { debounceTime, from, pipe, switchMap, tap } from 'rxjs';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FilterChips } from '../../shared/components/filter-chips';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { StatTile } from '../../shared/components/stat-tile';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';

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
  withState({
    filter: { status: '', search: '' } as OpeningFilter,
    status: 'idle' as 'idle' | 'pending' | 'fulfilled' | 'error',
    error: null as string | null,
  }),
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
              error: (error: HttpErrorResponse) =>
                patchState(store, setError(problemMessage(error))),
            }),
          ),
        ),
      ),
    ),
  })),
);

@Component({
  selector: 'app-openings',
  imports: [
    BoundField,
    Card,
    DataTable,
    FilterChips,
    FormField,
    PageHeader,
    StatTile,
    Button,
    FieldControl,
  ],
  providers: [OpeningsStore],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Openings</ng-container>
        <ng-container pageSubtitle i18n>{{ store.count() }} shown</ng-container>
      </app-page-header>

      <div class="grid gap-3 sm:grid-cols-3">
        <app-stat-tile
          label="Shown"
          [value]="store.count()"
          context="Matching the current filter"
        />
      </div>

      <app-filter-chips
        [chips]="chips"
        [value]="store.filter().status"
        (valueChange)="store.setFilter({ status: $event })"
      />

      <app-form-field class="max-w-xl" label="Search">
        <input
          appControl
          type="search"
          [value]="store.filter().search"
          (input)="store.setFilter({ search: $any($event.target).value })"
          i18n-placeholder
          placeholder="Title or opening code"
        />
      </app-form-field>

      @if (store.error(); as message) {
        <p class="text-sm text-danger" role="alert">{{ message }}</p>
      }

      <app-data-table
        [columns]="columns"
        [rows]="store.entities()"
        [track]="track"
        [empty]="copy.emptyOpenings"
      />

      <app-card>
        <h2 i18n>New opening</h2>
        <form class="grid gap-3 md:grid-cols-2" (submit)="create($event)">
          <app-form-field label="Title" [required]="true">
            <input appControl [formField]="draft.title" />
          </app-form-field>
          <app-form-field label="Positions" [required]="true">
            <input appControl type="number" [formField]="draft.positions" />
          </app-form-field>
          <app-form-field label="Hiring unit" [required]="true">
            <select appControl [formField]="draft.hiringUnitId">
              <option value="">Select</option>
              @for (unit of units(); track unit.id) {
                <option [value]="unit.id">{{ unit.name }}</option>
              }
            </select>
          </app-form-field>
          <app-form-field label="Function" [required]="true">
            <select appControl [formField]="draft.functionId">
              <option value="">Select</option>
              @for (item of functions(); track item.id) {
                <option [value]="item.id">{{ item.name }}</option>
              }
            </select>
          </app-form-field>
          <button appButton class="md:col-span-2" [disabled]="draft().invalid()" i18n>
            Create opening
          </button>
          @if (formError()) {
            <p class="text-sm text-danger md:col-span-2" role="alert">{{ formError() }}</p>
          }
        </form>
      </app-card>
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
  protected readonly chips = [
    { value: '', label: 'All' },
    { value: 'Open', label: 'Open' },
    { value: 'Draft', label: 'Draft' },
    { value: 'OnHold', label: 'On hold' },
  ];
  protected readonly columns: DataColumn<OpeningSummary>[] = [
    {
      header: 'Opening',
      value: (row) => row.title,
      detail: (row) => row.code,
      href: (row) => `/openings/${row.id}`,
    },
    { header: 'For', value: (row) => row.hiringUnitName },
    { header: 'Function', value: (row) => row.functionName },
    { header: 'Positions', value: (row) => String(row.positions) },
    { header: 'Applications', value: (row) => String(row.applicationCount) },
    { header: 'Status', value: (row) => row.status, status: true },
  ];
  protected readonly track = (row: OpeningSummary) => row.id;

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
      await this.api.send(
        'POST',
        '/api/core/v1/openings',
        {
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
        },
        true,
      );
      this.model.set({ title: '', positions: 1, hiringUnitId: '', functionId: '' });
      this.store.load(this.store.filter());
    } catch (error) {
      this.formError.set(problemMessage(error));
    }
  }
}
