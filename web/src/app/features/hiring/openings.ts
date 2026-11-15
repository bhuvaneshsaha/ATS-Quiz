import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { form, FormField as BoundField, required } from '@angular/forms/signals';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { setAllEntities, withEntities } from '@ngrx/signals/entities';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tapResponse } from '@ngrx/operators';
import { debounceTime, from, pipe, switchMap, tap } from 'rxjs';
import { Api, problemMessage } from '../../core/api';
import { SessionStore } from '../../core/session';
import { en } from '../../../locale/en';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FilterChips } from '../../shared/components/filter-chips';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { Button } from '../../shared/ui/button';
import { Combobox, ComboOption } from '../../shared/ui/combobox';
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
  hiringUnitId: string;
  functionId: string;
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
    filter: { status: '', search: '', hiringUnitId: '', functionId: '' } as OpeningFilter,
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
        switchMap(() =>
          from(api.get<{ items: OpeningSummary[] }>('/api/core/v1/openings?limit=50')).pipe(
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
  imports: [BoundField, Card, DataTable, FilterChips, FormField, PageHeader, Button, Combobox, FieldControl],
  providers: [OpeningsStore],
  template: `
    <section class="page">
      @if (creating()) {
        <a href="" (click)="cancelCreate($event)" i18n>Back to openings</a>
        <app-page-header>
          <ng-container pageTitle i18n>New opening</ng-container>
          <button
            appButton
            variant="secondary"
            type="button"
            [disabled]="draft().invalid()"
            (click)="create(false)"
            i18n
          >
            Save as draft
          </button>
          <button
            appButton
            type="button"
            [disabled]="draft().invalid()"
            (click)="create(true)"
            i18n
          >
            Open for hiring
          </button>
        </app-page-header>
        <form class="flex flex-col gap-5" (submit)="$event.preventDefault()">
          <app-card>
            <h2 i18n>Who is it for?</h2>
            <div class="grid gap-3 md:grid-cols-2">
              <app-form-field label="Hiring unit" [required]="true" [error]="unitError()">
                <app-combobox
                  name="hiringUnitId"
                  [options]="unitOptions()"
                  [value]="model().hiringUnitId"
                  placeholder="Type a hiring unit"
                  (valueChange)="setChoice('hiringUnitId', $event)"
                />
              </app-form-field>
              <app-form-field label="Function" [required]="true" [error]="functionError()">
                <app-combobox
                  name="functionId"
                  [options]="functionOptions()"
                  [value]="model().functionId"
                  placeholder="Type a function"
                  (valueChange)="setChoice('functionId', $event)"
                />
              </app-form-field>
            </div>
            <p class="text-sm text-muted" i18n>
              Every opening belongs to one hiring unit and one function.
            </p>
          </app-card>
          <app-card>
            <h2 i18n>Role</h2>
            <div class="grid gap-3 md:grid-cols-2">
              <app-form-field class="md:col-span-2" label="Title" [required]="true">
                <input appControl [formField]="draft.title" />
              </app-form-field>
              <app-form-field label="Location">
                <input appControl [formField]="draft.location" />
              </app-form-field>
              <app-form-field label="Positions" [required]="true">
                <input appControl type="number" [formField]="draft.positions" />
              </app-form-field>
              <app-form-field label="Seniority">
                <input appControl [formField]="draft.seniority" />
              </app-form-field>
              <app-form-field label="Reason for opening">
                <input appControl [formField]="draft.reason" />
              </app-form-field>
              <app-form-field label="Target fill date">
                <input appControl type="date" [formField]="draft.targetDate" />
              </app-form-field>
            </div>
          </app-card>
          @if (formError()) {
            <p class="text-sm text-danger" role="alert">{{ formError() }}</p>
          }
        </form>
      } @else {
        <app-page-header>
          <ng-container pageTitle i18n>Openings</ng-container>
          <ng-container pageSubtitle>{{ subtitle() }}</ng-container>
          @if (canManage()) {
            <button appButton type="button" (click)="creating.set(true)" i18n>New opening</button>
          }
        </app-page-header>

        <app-filter-chips
          [chips]="chips()"
          [value]="store.filter().status"
          (valueChange)="store.setFilter({ status: $event })"
        />

        <div class="flex flex-wrap items-end gap-2.5">
          <app-form-field class="min-w-60 flex-1" label="Search">
            <input
              appControl
              type="search"
              [value]="store.filter().search"
              (input)="store.setFilter({ search: $any($event.target).value })"
              i18n-placeholder
              placeholder="Title or opening ID"
            />
          </app-form-field>
          <app-form-field class="min-w-44 flex-1" label="Hiring unit">
            <app-combobox
              [options]="unitOptions()"
              [value]="store.filter().hiringUnitId"
              placeholder="All units"
              (valueChange)="store.setFilter({ hiringUnitId: $event })"
            />
          </app-form-field>
          <app-form-field class="min-w-44 flex-1" label="Function">
            <app-combobox
              [options]="functionOptions()"
              [value]="store.filter().functionId"
              placeholder="All functions"
              (valueChange)="store.setFilter({ functionId: $event })"
            />
          </app-form-field>
        </div>

        @if (store.status() === 'pending' && store.entities().length === 0) {
          <p class="text-sm text-muted" role="status">Loading openings.</p>
        } @else if (store.error(); as message) {
          <p class="text-sm text-danger" role="alert">{{ message }}</p>
        } @else {
          <app-data-table
            [columns]="columns"
            [rows]="visible()"
            [track]="track"
            [empty]="emptyMessage()"
            note="Applications is the number of people on the opening. Filled positions are counted on the opening itself."
          />
        }
      }
    </section>
  `,
})
export class OpeningsPage {
  protected readonly store = inject(OpeningsStore);
  protected readonly copy = en;
  private readonly api = inject(Api);
  private readonly session = inject(SessionStore);
  protected readonly units = signal<{ id: string; name: string; type: string }[]>([]);
  protected readonly functions = signal<{ id: string; name: string }[]>([]);
  protected readonly creating = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly unitError = signal<string | null>(null);
  protected readonly functionError = signal<string | null>(null);
  protected readonly unitOptions = computed<ComboOption[]>(() =>
    this.units().map((unit) => ({ value: unit.id, label: unit.name, hint: unit.type })),
  );
  protected readonly functionOptions = computed<ComboOption[]>(() =>
    this.functions().map((item) => ({ value: item.id, label: item.name })),
  );
  protected readonly model = signal({
    title: '',
    positions: 1,
    hiringUnitId: '',
    functionId: '',
    location: '',
    seniority: '',
    reason: '',
    targetDate: '',
  });
  protected readonly draft = form(this.model, (path) => {
    required(path.title);
    required(path.hiringUnitId);
    required(path.functionId);
  });
  protected readonly visible = computed(() => {
    const filter = this.store.filter();
    const search = filter.search.trim().toLowerCase();
    return this.store.entities().filter((row) => {
      if (filter.status && row.status !== filter.status) {
        return false;
      }
      if (filter.hiringUnitId && row.hiringUnitId !== filter.hiringUnitId) {
        return false;
      }
      if (filter.functionId && row.functionId !== filter.functionId) {
        return false;
      }
      if (!search) {
        return true;
      }
      return `${row.title} ${row.code} ${row.hiringUnitName}`.toLowerCase().includes(search);
    });
  });
  protected readonly chips = computed(() => {
    const rows = this.store.entities();
    const count = (status: string) => rows.filter((row) => row.status === status).length;
    return [
      { value: '', label: 'All', count: rows.length },
      { value: 'Open', label: 'Open', count: count('Open') },
      { value: 'Draft', label: 'Draft', count: count('Draft') },
      { value: 'OnHold', label: 'On hold', count: count('OnHold') },
    ];
  });
  protected readonly emptyMessage = computed(() => {
    const filter = this.store.filter();
    const narrowed = filter.status || filter.search.trim() || filter.hiringUnitId || filter.functionId;
    return narrowed ? this.copy.emptyOpenings : 'No openings yet.';
  });
  protected readonly subtitle = computed(() => {
    const rows = this.store.entities();
    const open = rows.filter((row) => row.status === 'Open' || row.status === 'Draft').length;
    return `${open} open or draft · ${rows.length} in this list`;
  });
  protected readonly columns: DataColumn<OpeningSummary>[] = [
    {
      header: 'Opening',
      value: (row) => row.title,
      detail: (row) => row.code,
      href: (row) => `/openings/${row.id}`,
    },
    {
      header: 'For',
      value: (row) => row.hiringUnitName,
      badge: (row) => this.unitType(row.hiringUnitId),
    },
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

  private unitType(id: string): string | null {
    return this.units().find((unit) => unit.id === id)?.type ?? null;
  }

  protected canManage(): boolean {
    return this.session.can('Core.Openings.Manage');
  }

  protected setChoice(field: 'hiringUnitId' | 'functionId', value: string): void {
    this.model.update((current) => ({ ...current, [field]: value }));
  }

  private async loadChoices(): Promise<void> {
    const [units, functions] = await Promise.allSettled([
      this.api.get<{ id: string; name: string; type: string }[]>('/api/core/v1/hiring-units'),
      this.api.get<{ id: string; name: string }[]>('/api/core/v1/functions'),
    ]);
    if (units.status === 'fulfilled') {
      this.units.set(units.value);
    } else {
      this.unitError.set(problemMessage(units.reason));
    }
    if (functions.status === 'fulfilled') {
      this.functions.set(functions.value);
    } else {
      this.functionError.set(problemMessage(functions.reason));
    }
  }

  protected cancelCreate(event: Event): void {
    event.preventDefault();
    this.creating.set(false);
  }

  protected async create(open: boolean): Promise<void> {
    if (this.draft().invalid()) {
      return;
    }
    this.formError.set(null);
    const value = this.model();
    try {
      const created = await this.api.send<{ id: string }>(
        'POST',
        '/api/core/v1/openings',
        {
          title: value.title,
          hiringUnitId: value.hiringUnitId,
          functionId: value.functionId,
          positions: Number(value.positions) || 1,
          location: value.location || null,
          seniority: value.seniority || null,
          reason: value.reason || null,
          targetDate: value.targetDate || null,
          hiringManagerId: null,
          recruiterId: null,
          externalId: null,
        },
        true,
      );
      if (open) {
        await this.api.send('POST', `/api/core/v1/openings/${created.id}/status`, {
          status: 'Open',
        });
      }
      this.model.set({
        title: '',
        positions: 1,
        hiringUnitId: '',
        functionId: '',
        location: '',
        seniority: '',
        reason: '',
        targetDate: '',
      });
      this.creating.set(false);
      this.store.load(this.store.filter());
    } catch (error) {
      this.formError.set(problemMessage(error));
    }
  }
}
