import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';

interface Named { id: string; name: string }
interface QuestionRow { id: string; status: string; currentVersionId: string; content: { prompt?: string } }
interface AssessmentRow { id: string; name: string; status: string; current: { id: string; isPublished: boolean } | null }

@Component({
  selector: 'app-assessments',
  template: `
    <section class="mx-auto max-w-5xl px-4 py-6">
      <h1 class="font-serif text-3xl" i18n>Assessments</h1>
      <ul class="mt-6 space-y-2 text-sm">
        @for (item of rows(); track item.id) {
          <li class="flex items-center justify-between rounded-xl border border-line bg-card px-3 py-3">
            <span>{{ item.name }} · {{ item.status }}</span>
            @if (item.status !== 'Published') { <button class="h-11 rounded-lg bg-teal px-3 text-card" type="button" (click)="publish(item.id)" i18n>Publish</button> }
          </li>
        }
      </ul>
      <form class="mt-8 space-y-3 rounded-2xl border border-line bg-card p-4" (submit)="create($event)">
        <h2 class="font-serif text-xl" i18n>New test</h2>
        <select class="h-11 w-full rounded-lg border border-line px-3" name="function" (change)="loadQuestions($any($event.target).value)" required>
          <option value="" i18n>Function</option>
          @for (item of functions(); track item.id) { <option [value]="item.id">{{ item.name }}</option> }
        </select>
        <input class="h-11 w-full rounded-lg border border-line px-3" name="name" required i18n-placeholder placeholder="Assessment name" />
        <select class="h-11 w-full rounded-lg border border-line px-3" name="question" required>
          <option value="" i18n>Published question</option>
          @for (question of questions(); track question.id) {
            @if (question.status === 'Published') { <option [value]="question.currentVersionId">{{ question.content.prompt }}</option> }
          }
        </select>
        <button class="h-11 rounded-lg bg-ink px-4 text-card" i18n>Create draft</button>
      </form>
      @if (error()) { <p class="mt-4 text-sm text-clay" role="alert">{{ error() }}</p> }
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
    this.questions.set(await this.api.get<QuestionRow[]>(`/api/assessments/v1/questions?functionId=${functionId}`));
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    this.error.set(null);
    try {
      await this.api.send('POST', '/api/assessments/v1/assessments', {
        functionId: data.get('function'),
        name: data.get('name'),
        passMark: 50,
        desktopOnly: false,
        sections: [{
          name: 'Main',
          minutes: 20,
          weight: 1,
          source: 'fixed',
          questionVersionIds: [data.get('question')],
          drawCount: null,
          drawTypes: null,
          skillIds: null,
        }],
      }, true);
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
