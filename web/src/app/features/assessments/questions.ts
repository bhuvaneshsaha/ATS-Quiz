import { Component, computed, inject, signal } from '@angular/core';
import { ComboOption } from '../../shared/ui/combobox';
import { Api, problemMessage } from '../../core/api';
import { SessionStore } from '../../core/session';
import { en } from '../../../locale/en';
import { Button } from '../../shared/ui/button';
import { Combobox } from '../../shared/ui/combobox';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { FilterChips } from '../../shared/components/filter-chips';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { typeLabel } from '../take/paper';
import { DraftOption, QuestionDraft, buildQuestionContent, emptyDraft } from './question-content';

interface Named {
  id: string;
  name: string;
}
interface QuestionRow {
  id: string;
  functionId: string;
  functionName?: string;
  status: string;
  type: string;
  points: number;
  content: { prompt?: string };
}

@Component({
  selector: 'app-questions',
  imports: [Button, Combobox, FieldControl, Card, DataTable, FilterChips, FormField, PageHeader],
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
        @if (session.can('Core.Functions.View')) {
          <app-form-field class="min-w-44 flex-1" label="Function">
            <app-combobox
              [options]="functionOptions()"
              [value]="functionId()"
              placeholder="All functions"
              (valueChange)="functionId.set($event)"
            />
          </app-form-field>
        }
      </div>

      @if (!(error() && questions().length === 0)) {
        <app-data-table
          [columns]="columns"
          [rows]="visible()"
          [track]="track"
          [empty]="emptyMessage()"
          (activated)="advance($event)"
        />
      }

      @if (canWrite()) {
        <app-card>
          <h2 i18n>New question</h2>
          <form class="flex flex-col gap-3" (submit)="create($event)">
            <app-form-field label="Function" [required]="true" [error]="fieldError()" errorId="function-error">
              <app-combobox
                name="functionId"
                [options]="functionOptions()"
                [value]="createFunctionId()"
                [invalid]="!!fieldError()"
                describedBy="function-error"
                placeholder="Type a function"
                (valueChange)="createFunctionId.set($event)"
              />
            </app-form-field>
            <app-form-field label="Type" [required]="true">
              <select appControl [value]="draft().type" (change)="setType($any($event.target).value)">
                <option value="single_choice">Single choice</option>
                <option value="multiple_choice">Multiple choice</option>
                <option value="numeric">Numeric</option>
                <option value="long_answer">Written answer</option>
                <option value="ordering">Ordering</option>
                <option value="matching">Matching</option>
              </select>
            </app-form-field>
            <app-form-field label="Prompt" [required]="true" [error]="contentError()" errorId="prompt-error">
              <textarea
                id="question-prompt"
                appControl
                rows="3"
                [attr.aria-invalid]="contentError() ? true : null"
                [attr.aria-describedby]="contentError() ? 'prompt-error' : null"
                [value]="draft().prompt"
                (input)="patch({ prompt: $any($event.target).value })"
              ></textarea>
            </app-form-field>
            <app-form-field label="Points" [required]="true">
              <input
                appControl
                type="number"
                min="0.5"
                step="0.5"
                [value]="draft().points"
                (input)="patch({ points: num($any($event.target).value) })"
              />
            </app-form-field>

            @if (draft().type === 'single_choice' || draft().type === 'multiple_choice') {
              @for (option of draft().options; track option.id) {
                <div class="flex flex-wrap items-end gap-2">
                  <app-form-field class="min-w-48 flex-1" [label]="'Option ' + ($index + 1)" [required]="true">
                    <input appControl [value]="option.text" (input)="setOption(option.id, $any($event.target).value)" />
                  </app-form-field>
                  <label class="flex min-h-11 items-center gap-2">
                    <input
                      [type]="draft().type === 'single_choice' ? 'radio' : 'checkbox'"
                      name="correct"
                      [checked]="option.correct"
                      (change)="setCorrect(option.id, $any($event.target).checked)"
                    />
                    <span i18n>Correct</span>
                  </label>
                </div>
              }
              <button appButton variant="secondary" type="button" (click)="addOption()" i18n>Add option</button>
              @if (draft().type === 'multiple_choice') {
                <app-form-field label="Partial credit">
                  <select appControl [value]="draft().partialCredit" (change)="patch({ partialCredit: $any($event.target).value })">
                    <option value="none">All or nothing</option>
                    <option value="perCorrectOption">Per correct option</option>
                  </select>
                </app-form-field>
              }
            }

            @if (draft().type === 'numeric') {
              <app-form-field label="Correct value" [required]="true">
                <input appControl type="number" step="any" [value]="draft().correctValue" (input)="patch({ correctValue: $any($event.target).value })" />
              </app-form-field>
              <app-form-field label="Tolerance">
                <input appControl type="number" min="0" step="any" [value]="draft().tolerance" (input)="patch({ tolerance: $any($event.target).value })" />
              </app-form-field>
            }

            @if (draft().type === 'long_answer') {
              <app-form-field label="Rubric">
                <textarea appControl rows="3" [value]="draft().rubric" (input)="patch({ rubric: $any($event.target).value })"></textarea>
              </app-form-field>
            }

            @if (draft().type === 'ordering') {
              @for (item of draft().items; track $index) {
                <app-form-field [label]="'Item ' + ($index + 1) + ' in the correct order'" [required]="true">
                  <input appControl [value]="item" (input)="setItem($index, $any($event.target).value)" />
                </app-form-field>
              }
              <button appButton variant="secondary" type="button" (click)="addItem()" i18n>Add item</button>
              <app-form-field label="Partial credit">
                <select appControl [value]="draft().partialCredit" (change)="patch({ partialCredit: $any($event.target).value })">
                  <option value="none">All or nothing</option>
                  <option value="perPosition">Per position</option>
                  <option value="perAdjacentPair">Per adjacent pair</option>
                </select>
              </app-form-field>
            }

            @if (draft().type === 'matching') {
              @for (pair of draft().pairs; track $index) {
                <div class="grid gap-2 md:grid-cols-2">
                  <app-form-field [label]="'Left ' + ($index + 1)" [required]="true">
                    <input appControl [value]="pair.left" (input)="setPair($index, 'left', $any($event.target).value)" />
                  </app-form-field>
                  <app-form-field [label]="'Right ' + ($index + 1)" [required]="true">
                    <input appControl [value]="pair.right" (input)="setPair($index, 'right', $any($event.target).value)" />
                  </app-form-field>
                </div>
              }
              <button appButton variant="secondary" type="button" (click)="addPair()" i18n>Add pair</button>
              <app-form-field label="Partial credit">
                <select appControl [value]="draft().partialCredit" (change)="patch({ partialCredit: $any($event.target).value })">
                  <option value="none">All or nothing</option>
                  <option value="perCorrectOption">Per correct pair</option>
                </select>
              </app-form-field>
            }

            <button appButton class="self-start" i18n>Save draft</button>
          </form>
        </app-card>
      }
      @if (notice()) {
        <p id="question-notice" class="text-sm text-ink-soft" role="status" tabindex="-1">{{ notice() }}</p>
      }
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class QuestionsPage {
  private readonly api = inject(Api);
  protected readonly session = inject(SessionStore);
  protected readonly copy = en;
  protected readonly functions = signal<Named[]>([]);
  protected readonly questions = signal<QuestionRow[]>([]);
  protected readonly functionId = signal('');
  protected readonly createFunctionId = signal('');
  protected readonly fieldError = signal<string | null>(null);
  protected readonly contentError = signal<string | null>(null);
  protected readonly functionOptions = computed<ComboOption[]>(() =>
    this.functions().map((item) => ({ value: item.id, label: item.name })),
  );
  protected readonly status = signal('');
  protected readonly search = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly draft = signal<QuestionDraft>(emptyDraft());
  protected readonly canWrite = computed(() => this.session.can('Assessments.Questions.Write'));
  protected readonly columns: DataColumn<QuestionRow>[] = [
    { header: 'Question', value: (row) => row.content.prompt ?? '', href: (row) => `/questions/${row.id}` },
    { header: 'Type', value: (row) => typeLabel(row.type) },
    { header: 'Points', value: (row) => String(row.points) },
    { header: 'Function', value: (row) => row.functionName || this.functionName(row.functionId) },
    { header: 'Status', value: (row) => row.status, status: true },
    {
      header: 'Action',
      value: () => '',
      action: (row) => {
        if (row.status === 'Draft' && this.session.can('Assessments.Questions.Write')) {
          return 'Submit';
        }
        return null;
      },
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
    () => `${this.questions().length} questions · open a question to review it before it is published`,
  );
  protected readonly emptyMessage = computed(() =>
    this.questions().length === 0 ? en.emptyQuestions : 'No questions match this view.',
  );

  constructor() {
    if (this.session.can('Core.Functions.View')) {
      void this.api
        .get<Named[]>('/api/core/v1/functions')
        .then((rows) => this.functions.set(rows))
        .catch((error) => this.error.set(problemMessage(error)));
    }
    void this.reload();
  }

  protected functionName(id: string): string {
    return this.functions().find((item) => item.id === id)?.name ?? '';
  }

  protected num(value: string): number {
    return Number(value);
  }

  protected patch(partial: Partial<QuestionDraft>): void {
    this.draft.update((current) => ({ ...current, ...partial }));
  }

  protected setType(type: string): void {
    this.draft.update((current) => ({ ...current, type, partialCredit: 'none' }));
  }

  protected setOption(id: string, text: string): void {
    this.draft.update((current) => ({
      ...current,
      options: current.options.map((option) => (option.id === id ? { ...option, text } : option)),
    }));
  }

  protected setCorrect(id: string, correct: boolean): void {
    this.draft.update((current) => ({
      ...current,
      options: current.options.map((option) =>
        current.type === 'single_choice'
          ? { ...option, correct: option.id === id }
          : option.id === id
            ? { ...option, correct }
            : option,
      ),
    }));
  }

  protected addOption(): void {
    this.draft.update((current) => ({
      ...current,
      options: [...current.options, { id: crypto.randomUUID(), text: '', correct: false } satisfies DraftOption],
    }));
  }

  protected setItem(index: number, text: string): void {
    this.draft.update((current) => ({
      ...current,
      items: current.items.map((item, itemIndex) => (itemIndex === index ? text : item)),
    }));
  }

  protected addItem(): void {
    this.draft.update((current) => ({ ...current, items: [...current.items, ''] }));
  }

  protected setPair(index: number, side: 'left' | 'right', text: string): void {
    this.draft.update((current) => ({
      ...current,
      pairs: current.pairs.map((pair, pairIndex) => (pairIndex === index ? { ...pair, [side]: text } : pair)),
    }));
  }

  protected addPair(): void {
    this.draft.update((current) => ({ ...current, pairs: [...current.pairs, { left: '', right: '' }] }));
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    const form = event.target as HTMLFormElement;
    const functionId = this.createFunctionId();
    this.error.set(null);
    this.fieldError.set(null);
    this.contentError.set(null);
    if (!functionId) {
      this.fieldError.set('Choose a function.');
      return;
    }
    const built = buildQuestionContent(this.draft());
    if ('error' in built) {
      this.contentError.set(built.error);
      queueMicrotask(() => document.getElementById('question-prompt')?.focus());
      return;
    }
    try {
      await this.api.send(
        'POST',
        '/api/assessments/v1/questions',
        {
          functionId,
          points: this.draft().points,
          timeLimitSeconds: null,
          skillIds: [],
          content: built.content,
        },
        true,
      );
      this.draft.set(emptyDraft());
      this.createFunctionId.set('');
      form.reset();
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async advance(question: QuestionRow): Promise<void> {
    this.error.set(null);
    try {
      if (question.status === 'Draft') {
        await this.api.send('POST', `/api/assessments/v1/questions/${question.id}/submit`, {});
        this.notice.set('Submitted for review.');
        queueMicrotask(() => document.getElementById('question-notice')?.focus());
      }
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private async reload(): Promise<void> {
    try {
      this.questions.set(await this.api.get<QuestionRow[]>('/api/assessments/v1/questions'));
      this.error.set(null);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
