import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
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
  currentVersionId: string;
  content: { prompt?: string };
}
interface AssessmentRow {
  id: string;
  name: string;
  status: string;
  current: { id: string; isPublished: boolean } | null;
}

@Component({
  selector: 'app-assessments',
  imports: [Button, FieldControl, Card, FormField, PageHeader, StatusPill],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Assessments</ng-container>
        <ng-container pageSubtitle i18n
          >A published version stays pinned to the openings that use it</ng-container
        >
      </app-page-header>
      <ul class="flex flex-col gap-2">
        @for (item of rows(); track item.id) {
          <li
            class="flex flex-wrap items-center justify-between gap-3 rounded-card border border-line bg-surface px-4 py-3"
          >
            <span class="font-medium">{{ item.name }}</span>
            <span class="flex items-center gap-2">
              <app-status-pill [status]="item.status" />
              @if (item.status !== 'Published') {
                <button appButton type="button" (click)="publish(item.id)" i18n>Publish</button>
              }
            </span>
          </li>
        }
      </ul>
      <app-card>
        <h2 i18n>New test</h2>
        <form class="flex flex-col gap-3" (submit)="create($event)">
          <app-form-field label="Function" [required]="true">
            <select
              appControl
              name="function"
              required
              (change)="loadQuestions($any($event.target).value)"
            >
              <option value="" i18n>Function</option>
              @for (item of functions(); track item.id) {
                <option [value]="item.id">{{ item.name }}</option>
              }
            </select>
          </app-form-field>
          <app-form-field label="Assessment name" [required]="true">
            <input appControl name="name" required />
          </app-form-field>
          <app-form-field label="Published question" [required]="true">
            <select appControl name="question" required>
              <option value="" i18n>Published question</option>
              @for (question of questions(); track question.id) {
                @if (question.status === 'Published') {
                  <option [value]="question.currentVersionId">{{ question.content.prompt }}</option>
                }
              }
            </select>
          </app-form-field>
          <button appButton i18n>Create draft</button>
        </form>
      </app-card>
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class AssessmentsPage {
  private readonly api = inject(Api);
  protected readonly functions = signal<Named[]>([]);
  protected readonly questions = signal<QuestionRow[]>([]);
  protected readonly rows = signal<AssessmentRow[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.api.get<Named[]>('/api/core/v1/functions').then((rows) => this.functions.set(rows));
    void this.reload();
  }

  private async reload(): Promise<void> {
    this.rows.set(await this.api.get<AssessmentRow[]>('/api/assessments/v1/assessments'));
  }

  protected async loadQuestions(functionId: string): Promise<void> {
    if (!functionId) {
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
        '/api/assessments/v1/assessments',
        {
          functionId: data.get('function'),
          name: data.get('name'),
          passMark: 50,
          desktopOnly: false,
          sections: [
            {
              name: 'Main',
              minutes: 20,
              weight: 1,
              source: 'fixed',
              questionVersionIds: [data.get('question')],
              drawCount: null,
              drawTypes: null,
              skillIds: null,
            },
          ],
        },
        true,
      );
      (event.target as HTMLFormElement).reset();
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async publish(id: string): Promise<void> {
    await this.api.send('POST', `/api/assessments/v1/assessments/${id}/publish`, {});
    await this.reload();
  }
}
