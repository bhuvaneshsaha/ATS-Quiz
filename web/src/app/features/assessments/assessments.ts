import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { SessionStore } from '../../core/session';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';

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
    sections: { minutes: number; questionVersionIds: string[] }[];
  } | null;
}

@Component({
  selector: 'app-assessments',
  imports: [Button, FieldControl, Card, DataTable, FormField, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Assessments</ng-container>
        <ng-container pageSubtitle i18n>A published version stays pinned to the openings that use it</ng-container>
      </app-page-header>
      <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
        <app-data-table
          class="min-w-0 flex-1"
          [columns]="columns"
          [rows]="rows()"
          [track]="track"
          empty="No assessments yet."
          (activated)="publish($event.id)"
        />
        @if (canWrite()) {
          <app-card class="w-full lg:w-[28rem]">
            <h2 i18n>New assessment</h2>
            <form class="flex flex-col gap-3" (submit)="create($event)">
              <app-form-field label="Function" [required]="true">
                <select appControl name="function" required (change)="loadQuestions($any($event.target).value)">
                  <option value="" i18n>Function</option>
                  @for (item of functions(); track item.id) {
                    <option [value]="item.id">{{ item.name }}</option>
                  }
                </select>
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
              @for (section of sections(); track $index) {
                <div class="flex flex-col gap-2 rounded-control border border-line p-3">
                  <div class="flex items-center justify-between gap-2">
                    <p class="font-medium">Section {{ $index + 1 }}</p>
                    @if (sections().length > 1) {
                      <button appButton variant="secondary" type="button" (click)="removeSection($index)">Remove</button>
                    }
                  </div>
                  <app-form-field label="Section name" [required]="true">
                    <input appControl [value]="section.name" (input)="patchSection($index, { name: $any($event.target).value })" />
                  </app-form-field>
                  <div class="grid gap-3 sm:grid-cols-2">
                    <app-form-field label="Minutes" [required]="true">
                      <input appControl type="number" min="1" [value]="section.minutes" (input)="patchSection($index, { minutes: num($any($event.target).value) })" />
                    </app-form-field>
                    <app-form-field label="Weight" [required]="true">
                      <input appControl type="number" min="0.1" step="0.1" [value]="section.weight" (input)="patchSection($index, { weight: num($any($event.target).value) })" />
                    </app-form-field>
                  </div>
                  <fieldset class="flex flex-col gap-2 border-0 p-0">
                    <legend class="text-sm text-muted" i18n>Published questions</legend>
                    @for (question of questions(); track question.id) {
                      @if (question.status === 'Published') {
                        <label class="flex min-h-11 items-start gap-2">
                          <input
                            type="checkbox"
                            [checked]="section.questionVersionIds.includes(question.currentVersionId)"
                            (change)="toggleQuestion($index, question.currentVersionId, $any($event.target).checked)"
                          />
                          <span>{{ question.content.prompt }}</span>
                        </label>
                      }
                    } @empty {
                      <p class="text-sm text-muted" i18n>Choose a function to see its published questions.</p>
                    }
                  </fieldset>
                </div>
              }
              <button appButton variant="secondary" type="button" (click)="addSection()" i18n>Add section</button>
              <button appButton i18n>Create draft</button>
            </form>
          </app-card>
        }
      </div>
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
  protected readonly name = signal('');
  protected readonly passMark = signal(50);
  protected readonly desktopOnly = signal(false);
  protected readonly sections = signal<SectionDraft[]>([{ name: 'Main', minutes: 20, weight: 1, questionVersionIds: [] }]);
  protected canWrite(): boolean {
    return this.session.can('Assessments.Assessments.Write');
  }
  protected readonly columns: DataColumn<AssessmentRow>[] = [
    { header: 'Assessment', value: (row) => row.name },
    {
      header: 'Pass mark',
      value: (row) => (row.current ? `${row.current.passMark}%` : ''),
    },
    {
      header: 'Time',
      value: (row) => {
        const minutes = row.current?.sections.reduce((sum, section) => sum + section.minutes, 0) ?? 0;
        return minutes ? `${minutes} min` : '';
      },
    },
    {
      header: 'Questions',
      value: (row) =>
        String(row.current?.sections.reduce((sum, section) => sum + section.questionVersionIds.length, 0) ?? 0),
    },
    { header: 'Status', value: (row) => row.status, status: true },
    {
      header: '',
      value: () => '',
      action: (row) =>
        row.status === 'Published' || !this.session.can('Assessments.Assessments.Publish') ? null : 'Publish',
    },
  ];
  protected readonly track = (row: AssessmentRow) => row.id;

  constructor() {
    void this.api
      .get<Named[]>('/api/core/v1/functions')
      .then((rows) => this.functions.set(rows))
      .catch((error) => this.error.set(problemMessage(error)));
    void this.reload();
  }

  protected async loadQuestions(functionId: string): Promise<void> {
    this.error.set(null);
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

  protected num(value: string): number {
    return Number(value);
  }

  protected addSection(): void {
    this.sections.update((current) => [...current, { name: `Section ${current.length + 1}`, minutes: 10, weight: 1, questionVersionIds: [] }]);
  }

  protected removeSection(index: number): void {
    this.sections.update((current) => current.filter((_, item) => item !== index));
  }

  protected patchSection(index: number, partial: Partial<SectionDraft>): void {
    this.sections.update((current) => current.map((section, item) => (item === index ? { ...section, ...partial } : section)));
  }

  protected toggleQuestion(index: number, versionId: string, checked: boolean): void {
    this.sections.update((current) =>
      current.map((section, item) => {
        if (item !== index) {
          return section;
        }
        const ids = checked
          ? [...section.questionVersionIds, versionId]
          : section.questionVersionIds.filter((id) => id !== versionId);
        return { ...section, questionVersionIds: ids };
      }),
    );
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    const functionId = data.get('function')?.toString() ?? '';
    this.error.set(null);
    const sections = this.sections();
    if (!functionId) {
      this.error.set('Choose a function.');
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
    try {
      await this.api.send(
        'POST',
        '/api/assessments/v1/assessments',
        {
          functionId,
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
      this.name.set('');
      this.passMark.set(50);
      this.desktopOnly.set(false);
      this.sections.set([{ name: 'Main', minutes: 20, weight: 1, questionVersionIds: [] }]);
      (event.target as HTMLFormElement).reset();
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async publish(id: string): Promise<void> {
    this.error.set(null);
    try {
      await this.api.send('POST', `/api/assessments/v1/assessments/${id}/publish`, {});
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private async reload(): Promise<void> {
    try {
      this.rows.set(await this.api.get<AssessmentRow[]>('/api/assessments/v1/assessments'));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
