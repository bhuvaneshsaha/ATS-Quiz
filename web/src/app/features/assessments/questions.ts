import { Component, computed, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FilterChips } from '../../shared/components/filter-chips';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';

interface Named {
  id: string;
  name: string;
}
interface QuestionRow {
  id: string;
  functionId: string;
  status: string;
  type: string;
  content: { prompt?: string };
}

@Component({
  selector: 'app-questions',
  imports: [Button, FieldControl, Card, DataTable, FilterChips, FormField, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Question bank</ng-container>
        <ng-container pageSubtitle>{{ subtitle() }}</ng-container>
      </app-page-header>

      <app-filter-chips [chips]="chips()" [value]="status()" (valueChange)="status.set($event)" />

      <div class="flex flex-wrap items-end gap-2.5">
        <app-form-field class="min-w-60 flex-1" label="Search">
          <input
            appControl
            type="search"
            [value]="search()"
            (input)="search.set($any($event.target).value)"
            i18n-placeholder
            placeholder="Search question text"
          />
        </app-form-field>
        <app-form-field class="min-w-44 flex-1" label="Function">
          <select
            appControl
            [value]="functionId()"
            (change)="functionId.set($any($event.target).value)"
          >
            <option value="" i18n>All functions</option>
            @for (item of functions(); track item.id) {
              <option [value]="item.id">{{ item.name }}</option>
            }
          </select>
        </app-form-field>
      </div>

      <app-data-table
        [columns]="columns"
        [rows]="visible()"
        [track]="track"
        [empty]="copy.emptyQuestions"
        (activated)="advance($event)"
      />

      <app-card>
        <h2 i18n>New question</h2>
        <form class="flex flex-col gap-3" (submit)="create($event)">
          <app-form-field label="Function" [required]="true">
            <select appControl name="functionId" required [value]="functionId()">
              <option value="" i18n>Select</option>
              @for (item of functions(); track item.id) {
                <option [value]="item.id">{{ item.name }}</option>
              }
            </select>
          </app-form-field>
          <app-form-field label="Prompt" [required]="true">
            <input appControl name="prompt" required />
          </app-form-field>
          <app-form-field label="Correct option" [required]="true">
            <input appControl name="correct" required />
          </app-form-field>
          <app-form-field label="Other option" [required]="true">
            <input appControl name="wrong" required />
          </app-form-field>
          <button appButton class="self-start" i18n>Save draft</button>
        </form>
      </app-card>
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class QuestionsPage {
  private readonly api = inject(Api);
  protected readonly copy = en;
  protected readonly functions = signal<Named[]>([]);
  protected readonly questions = signal<QuestionRow[]>([]);
  protected readonly functionId = signal('');
  protected readonly status = signal('');
  protected readonly search = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly columns: DataColumn<QuestionRow>[] = [
    { header: 'Question', value: (row) => row.content.prompt ?? '' },
    { header: 'Type', value: (row) => typeLabel(row.type) },
    { header: 'Function', value: (row) => this.functionName(row.functionId) },
    { header: 'Status', value: (row) => row.status, status: true },
    {
      header: '',
      value: () => '',
      action: (row) =>
        row.status === 'Draft' ? 'Submit' : row.status === 'InReview' ? 'Publish' : null,
    },
  ];
  protected readonly track = (row: QuestionRow) => row.id;
  protected readonly visible = computed(() => {
    const search = this.search().trim().toLowerCase();
    return this.questions().filter((row) => {
      if (this.status() && row.status !== this.status()) {
        return false;
      }
      if (this.functionId() && row.functionId !== this.functionId()) {
        return false;
      }
      if (!search) {
        return true;
      }
      return (row.content.prompt ?? '').toLowerCase().includes(search);
    });
  });
  protected readonly chips = computed(() => {
    const rows = this.questions();
    const count = (status: string) => rows.filter((row) => row.status === status).length;
    return [
      { value: '', label: 'All', count: rows.length },
      { value: 'Draft', label: 'Draft', count: count('Draft') },
      { value: 'InReview', label: 'In review', count: count('InReview') },
      { value: 'Published', label: 'Published', count: count('Published') },
    ];
  });
  protected readonly subtitle = computed(
    () => `${this.questions().length} questions · drafts stay editable until they are published`,
  );

  constructor() {
    void this.api.get<Named[]>('/api/core/v1/functions').then((rows) => this.functions.set(rows));
    void this.reload();
  }

  protected functionName(id: string): string {
    return this.functions().find((item) => item.id === id)?.name ?? '';
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    this.error.set(null);
    try {
      await this.api.send(
        'POST',
        '/api/assessments/v1/questions',
        {
          functionId: data.get('functionId'),
          points: 4,
          timeLimitSeconds: null,
          skillIds: [],
          content: {
            type: 'single_choice',
            prompt: data.get('prompt'),
            attachments: [],
            options: [
              { id: crypto.randomUUID(), text: data.get('correct'), isCorrect: true },
              { id: crypto.randomUUID(), text: data.get('wrong'), isCorrect: false },
            ],
          },
        },
        true,
      );
      (event.target as HTMLFormElement).reset();
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async advance(question: QuestionRow): Promise<void> {
    if (question.status === 'Draft') {
      await this.submit(question.id);
    }
    if (question.status === 'InReview') {
      await this.publish(question.id);
    }
  }

  protected async submit(id: string): Promise<void> {
    await this.api.send('POST', `/api/assessments/v1/questions/${id}/submit`, {});
    await this.reload();
  }

  protected async publish(id: string): Promise<void> {
    await this.api.send('POST', `/api/assessments/v1/questions/${id}/publish`, {});
    await this.reload();
  }

  private async reload(): Promise<void> {
    this.questions.set(await this.api.get<QuestionRow[]>('/api/assessments/v1/questions'));
  }
}

function typeLabel(type: string): string {
  const text = type.replaceAll('_', ' ');
  return text ? text.charAt(0).toUpperCase() + text.slice(1) : type;
}
