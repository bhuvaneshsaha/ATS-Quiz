import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { StatusPill } from '../../shared/components/status-pill';

interface Named {
  id: string;
  name: string;
}
interface QuestionRow {
  id: string;
  status: string;
  type: string;
  content: { prompt?: string };
}

@Component({
  selector: 'app-questions',
  imports: [Button, FieldControl, Card, FormField, PageHeader, StatusPill],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Question bank</ng-container>
        <ng-container pageSubtitle i18n>Drafts stay editable until they are published</ng-container>
      </app-page-header>
      <app-form-field label="Function" [required]="true">
        <select appControl class="md:max-w-xs" (change)="load($any($event.target).value)">
          <option value="">Select</option>
          @for (item of functions(); track item.id) {
            <option [value]="item.id">{{ item.name }}</option>
          }
        </select>
      </app-form-field>
      <ul class="flex flex-col gap-2">
        @for (question of questions(); track question.id) {
          <li
            class="flex flex-wrap items-center justify-between gap-3 rounded-card border border-line bg-surface px-4 py-3"
          >
            <span>{{ question.content.prompt }}</span>
            <span class="flex items-center gap-2">
              <app-status-pill [status]="question.status" />
              @if (question.status === 'Draft') {
                <button
                  appButton
                  variant="secondary"
                  type="button"
                  (click)="submit(question.id)"
                  i18n
                >
                  Submit
                </button>
              }
              @if (question.status === 'InReview') {
                <button appButton type="button" (click)="publish(question.id)" i18n>Publish</button>
              }
            </span>
          </li>
        } @empty {
          <li class="text-muted">{{ copy.emptyQuestions }}</li>
        }
      </ul>
      <app-card>
        <h2 i18n>Single choice</h2>
        <form class="flex flex-col gap-3" (submit)="create($event)">
          <app-form-field label="Prompt" [required]="true">
            <input appControl name="prompt" required />
          </app-form-field>
          <app-form-field label="Correct option" [required]="true">
            <input appControl name="correct" required />
          </app-form-field>
          <app-form-field label="Other option" [required]="true">
            <input appControl name="wrong" required />
          </app-form-field>
          <button appButton i18n>Save draft</button>
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
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.api.get<Named[]>('/api/core/v1/functions').then((rows) => this.functions.set(rows));
  }

  protected async load(functionId: string): Promise<void> {
    this.functionId.set(functionId);
    if (!functionId) {
      this.questions.set([]);
      return;
    }
    this.questions.set(
      await this.api.get<QuestionRow[]>(`/api/assessments/v1/questions?functionId=${functionId}`),
    );
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
          functionId: this.functionId(),
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
      await this.load(this.functionId());
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async submit(id: string): Promise<void> {
    await this.api.send('POST', `/api/assessments/v1/questions/${id}/submit`, {});
    await this.load(this.functionId());
  }

  protected async publish(id: string): Promise<void> {
    await this.api.send('POST', `/api/assessments/v1/questions/${id}/publish`, {});
    await this.load(this.functionId());
  }
}
