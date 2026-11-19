import { Component, computed, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { SessionStore } from '../../core/session';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Combobox, ComboOption } from '../../shared/ui/combobox';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { moveItem, toggleId } from './selection';

interface Named {
  id: string;
  name: string;
}
interface QuestionRow {
  id: string;
  status: string;
  currentVersionId: string;
  content: { prompt?: string };
}
interface SectionDraft {
  key: string;
  name: string;
  minutes: number;
  weight: number;
  questionVersionIds: string[];
}
interface AssessmentRow {
  id: string;
  name: string;
  status: string;
  current: {
    id: string;
    passMark: number;
    desktopOnly: boolean;
    isPublished: boolean;
    sections: { name: string; minutes: number; questionVersionIds: string[] }[];
  } | null;
}

function blankSection(name = 'Main', minutes = 20): SectionDraft {
  return { key: crypto.randomUUID(), name, minutes, weight: 1, questionVersionIds: [] };
}

@Component({
  selector: 'app-assessments',
  imports: [Button, FieldControl, Combobox, Card, DataTable, FormField, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Assessments</ng-container>
        <ng-container pageSubtitle i18n>A published version stays pinned to the openings that use it</ng-container>
      </app-page-header>
      @if (notice()) {
        <p class="text-sm text-ink-soft" role="status" tabindex="-1">{{ notice() }}</p>
      }
      <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
        <app-data-table
          class="min-w-0 flex-1"
          [columns]="columns"
          [rows]="rows()"
          [track]="track"
          empty="No assessments yet."
          (activated)="askPublish($event)"
        />
        @if (canWrite()) {
          <app-card class="w-full lg:w-[28rem]">
            <h2 i18n>New assessment</h2>
            <form class="flex flex-col gap-3" (submit)="create($event)">
              <app-form-field label="Function" [required]="true" [error]="fieldError()">
                <app-combobox
                  name="function"
                  [options]="functionOptions()"
                  [value]="functionId()"
                  placeholder="Type a function"
                  (valueChange)="chooseFunction($event)"
                />
              </app-form-field>
              <app-form-field label="Assessment name" [required]="true">
                <input appControl name="name" required [value]="name()" (input)="name.set($any($event.target).value)" />
              </app-form-field>
              <div class="grid gap-3 sm:grid-cols-2">
                <app-form-field label="Pass mark %" [required]="true">
                  <input appControl type="number" min="0" max="100" [value]="passMark()" (input)="passMark.set(num($any($event.target).value))" />
                </app-form-field>
                <label class="flex min-h-11 items-center gap-2 self-end">
                  <input type="checkbox" [checked]="desktopOnly()" (change)="desktopOnly.set($any($event.target).checked)" />
                  <span i18n>Desktop only</span>
                </label>
              </div>
              @for (section of sections(); track section.key; let sectionIndex = $index) {
                <div class="flex flex-col gap-2 rounded-control border border-line p-3">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <p class="font-medium">Section {{ sectionIndex + 1 }}</p>
                    <div class="flex gap-2">
                      <button appButton variant="secondary" type="button" (click)="moveSection(sectionIndex, -1)">Up</button>
                      <button appButton variant="secondary" type="button" (click)="moveSection(sectionIndex, 1)">Down</button>
                      @if (sections().length > 1) {
                        <button appButton variant="secondary" type="button" (click)="removeSection(sectionIndex)">Remove</button>
                      }
                    </div>
                  </div>
                  <app-form-field label="Section name" [required]="true">
                    <input appControl [value]="section.name" (input)="patchSection(sectionIndex, { name: $any($event.target).value })" />
                  </app-form-field>
                  <div class="grid gap-3 sm:grid-cols-2">
                    <app-form-field label="Minutes" [required]="true">
                      <input appControl type="number" min="1" [value]="section.minutes" (input)="patchSection(sectionIndex, { minutes: num($any($event.target).value) })" />
                    </app-form-field>
                    <app-form-field label="Weight" [required]="true">
                      <input appControl type="number" min="0.1" step="0.1" [value]="section.weight" (input)="patchSection(sectionIndex, { weight: num($any($event.target).value) })" />
                    </app-form-field>
                  </div>
                  <fieldset class="flex flex-col gap-2 border-0 p-0">
                    <legend class="text-sm text-muted">
                      Published questions · {{ section.questionVersionIds.length }} selected
                    </legend>
                    @for (question of published(); track question.currentVersionId) {
                      <div class="flex items-center gap-2">
                        <label class="flex min-h-11 flex-1 items-start gap-2">
                          <input
                            type="checkbox"
                            [checked]="section.questionVersionIds.includes(question.currentVersionId)"
                            (change)="toggleQuestion(sectionIndex, question.currentVersionId, $any($event.target).checked)"
                          />
                          <span>{{ question.content.prompt }}</span>
                        </label>
                        <button appButton variant="secondary" type="button" (click)="moveQuestion(sectionIndex, question.currentVersionId, -1)">Up</button>
                        <button appButton variant="secondary" type="button" (click)="moveQuestion(sectionIndex, question.currentVersionId, 1)">Down</button>
                      </div>
                    } @empty {
                      <p class="text-sm text-muted" i18n>Choose a function to see its published questions.</p>
                    }
                  </fieldset>
                </div>
              }
              <p class="text-sm text-ink-soft">{{ selectedCount() }} questions selected</p>
              <button appButton variant="secondary" type="button" (click)="addSection()" i18n>Add section</button>
              <button appButton variant="secondary" type="button" (click)="preview.set(!preview())">
                {{ preview() ? 'Hide preview' : 'Preview' }}
              </button>
              @if (preview()) {
                <div class="flex flex-col gap-2 rounded-control bg-canvas p-3">
                  <p class="font-medium">{{ name() || 'Untitled' }} · {{ passMark() }}% · {{ totalMinutes() }} min</p>
                  @for (section of sections(); track section.key) {
                    <div>
                      <p>{{ section.name }} · {{ section.minutes }} min</p>
                      <ol class="list-decimal pl-5">
                        @for (id of section.questionVersionIds; track id) {
                          <li>{{ promptFor(id) }}</li>
                        }
                      </ol>
                    </div>
                  }
                </div>
              }
              <button appButton i18n>Create draft</button>
            </form>
          </app-card>
        }
      </div>
      @if (pending(); as row) {
        <div class="flex flex-col gap-3 rounded-card border border-line bg-surface p-5" role="dialog" aria-labelledby="publish-title">
          <h2 id="publish-title">Publish {{ row.name }}?</h2>
          <p>
            {{ questionCount(row) }} questions · {{ minutes(row) }} min · pass mark {{ row.current?.passMark }}%
          </p>
          <div class="flex gap-2">
            <button appButton type="button" (click)="confirmPublish()">Publish</button>
            <button appButton variant="secondary" type="button" (click)="pending.set(null)">Cancel</button>
          </div>
        </div>
      }
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class AssessmentsPage {
  private readonly api = inject(Api);
  private readonly session = inject(SessionStore);
  protected readonly functions = signal<Named[]>([]);
  protected readonly questions = signal<QuestionRow[]>([]);
  protected readonly rows = signal<AssessmentRow[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly passMark = signal(50);
  protected readonly desktopOnly = signal(false);
  protected readonly functionId = signal('');
  protected readonly preview = signal(false);
  protected readonly pending = signal<AssessmentRow | null>(null);
  protected readonly sections = signal<SectionDraft[]>([blankSection()]);
  protected readonly functionOptions = computed<ComboOption[]>(() =>
    this.functions().map((item) => ({ value: item.id, label: item.name })),
  );
  protected readonly published = computed(() => this.questions().filter((question) => question.status === 'Published'));
  protected readonly selectedCount = computed(() =>
    this.sections().reduce((sum, section) => sum + section.questionVersionIds.length, 0),
  );
  protected readonly totalMinutes = computed(() => this.sections().reduce((sum, section) => sum + (section.minutes || 0), 0));
  protected canWrite(): boolean {
    return this.session.can('Assessments.Assessments.Write');
  }
  protected readonly columns: DataColumn<AssessmentRow>[] = [
    { header: 'Assessment', value: (row) => row.name },
    { header: 'Pass mark', value: (row) => (row.current ? `${row.current.passMark}%` : '') },
    { header: 'Time', value: (row) => (this.minutes(row) ? `${this.minutes(row)} min` : '') },
    { header: 'Questions', value: (row) => String(this.questionCount(row)) },
    { header: 'Status', value: (row) => row.status, status: true },
    {
      header: 'Action',
      value: () => '',
      action: (row) =>
        row.status === 'Published' || !this.session.can('Assessments.Assessments.Publish') ? null : 'Publish',
    },
  ];
  protected readonly track = (row: AssessmentRow) => row.id;

  constructor() {
    if (this.session.can('Core.Functions.View')) {
      void this.api
        .get<Named[]>('/api/core/v1/functions')
        .then((rows) => this.functions.set(rows))
        .catch((error) => this.error.set(problemMessage(error)));
    }
    void this.reload();
  }

  protected num(value: string): number {
    return Number(value);
  }

  protected minutes(row: AssessmentRow): number {
    return row.current?.sections.reduce((sum, section) => sum + section.minutes, 0) ?? 0;
  }

  protected questionCount(row: AssessmentRow): number {
    return row.current?.sections.reduce((sum, section) => sum + section.questionVersionIds.length, 0) ?? 0;
  }

  protected promptFor(id: string): string {
    return this.questions().find((question) => question.currentVersionId === id)?.content.prompt ?? 'Question';
  }

  protected chooseFunction(id: string): void {
    this.functionId.set(id);
    this.fieldError.set(null);
    void this.loadQuestions(id);
  }

  protected async loadQuestions(functionId: string): Promise<void> {
    if (!functionId) {
      this.questions.set([]);
      return;
    }
    try {
      this.questions.set(await this.api.get<QuestionRow[]>(`/api/assessments/v1/questions?functionId=${functionId}`));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected addSection(): void {
    this.sections.update((current) => [...current, blankSection(`Section ${current.length + 1}`, 10)]);
  }

  protected removeSection(index: number): void {
    this.sections.update((current) => current.filter((_, item) => item !== index));
  }

  protected moveSection(index: number, delta: number): void {
    this.sections.update((current) => moveItem(current, index, delta));
  }

  protected patchSection(index: number, partial: Partial<SectionDraft>): void {
    this.sections.update((current) => current.map((section, item) => (item === index ? { ...section, ...partial } : section)));
  }

  protected toggleQuestion(index: number, versionId: string, checked: boolean): void {
    this.sections.update((current) =>
      current.map((section, item) =>
        item === index ? { ...section, questionVersionIds: toggleId(section.questionVersionIds, versionId, checked) } : section,
      ),
    );
  }

  protected moveQuestion(index: number, versionId: string, delta: number): void {
    this.sections.update((current) =>
      current.map((section, item) => {
        if (item !== index) {
          return section;
        }
        const position = section.questionVersionIds.indexOf(versionId);
        if (position < 0) {
          return section;
        }
        return { ...section, questionVersionIds: moveItem(section.questionVersionIds, position, delta) };
      }),
    );
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set(null);
    this.fieldError.set(null);
    const sections = this.sections();
    if (!this.functionId()) {
      this.fieldError.set('Choose a function.');
      return;
    }
    if (!this.name().trim()) {
      this.error.set('Name the assessment.');
      return;
    }
    if (sections.some((section) => !section.name.trim() || section.minutes <= 0 || section.questionVersionIds.length === 0)) {
      this.error.set('Each section needs a name, a time limit, and at least one question.');
      return;
    }
    const selected = this.selectedCount();
    try {
      const created = await this.api.send<AssessmentRow>(
        'POST',
        '/api/assessments/v1/assessments',
        {
          functionId: this.functionId(),
          name: this.name().trim(),
          passMark: this.passMark(),
          desktopOnly: this.desktopOnly(),
          sections: sections.map((section) => ({
            name: section.name.trim(),
            minutes: section.minutes,
            weight: section.weight,
            source: 'fixed',
            questionVersionIds: section.questionVersionIds,
            drawCount: null,
            drawTypes: null,
            skillIds: null,
          })),
        },
        true,
      );
      if (this.questionCount(created) !== selected) {
        this.error.set('The saved test does not match your selection. It was not cleared.');
        await this.reload();
        return;
      }
      this.name.set('');
      this.passMark.set(50);
      this.desktopOnly.set(false);
      this.sections.set([blankSection()]);
      this.notice.set('Draft saved.');
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected askPublish(row: AssessmentRow): void {
    this.pending.set(row);
  }

  protected async confirmPublish(): Promise<void> {
    const row = this.pending();
    if (!row) {
      return;
    }
    this.error.set(null);
    try {
      await this.api.send('POST', `/api/assessments/v1/assessments/${row.id}/publish`, {});
      this.pending.set(null);
      this.notice.set(`Published ${row.name}.`);
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private async reload(): Promise<void> {
    try {
      this.rows.set(await this.api.get<AssessmentRow[]>('/api/assessments/v1/assessments'));
    } catch (error) {
      this.rows.set([]);
      this.error.set(problemMessage(error));
    }
  }
}
