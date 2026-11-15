import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';

interface Named { id: string; name: string }
interface QuestionRow { id: string; status: string; type: string; content: { prompt?: string } }

@Component({
  selector: 'app-questions',
  template: `
    <section class="mx-auto max-w-5xl px-4 py-6">
      <h1 class="font-serif text-3xl" i18n>Question bank</h1>
      <label class="mt-4 block text-sm" i18n>Function
        <select class="mt-1 h-11 w-full rounded-lg border border-line bg-card px-3 md:w-72" (change)="load($any($event.target).value)">
          <option value="">Select</option>
          @for (item of functions(); track item.id) { <option [value]="item.id">{{ item.name }}</option> }
        </select>
      </label>
      <ul class="mt-6 space-y-2">
        @for (question of questions(); track question.id) {
          <li class="flex items-center justify-between rounded-xl border border-line bg-card px-3 py-3 text-sm">
            <span>{{ question.content.prompt }} · {{ question.status }}</span>
            @if (question.status === 'Draft') { <button class="h-11 rounded-lg border border-line px-3" type="button" (click)="submit(question.id)" i18n>Submit</button> }
            @if (question.status === 'InReview') { <button class="h-11 rounded-lg bg-teal px-3 text-card" type="button" (click)="publish(question.id)" i18n>Publish</button> }
          </li>
        } @empty { <li class="text-sm text-ink/70">{{ copy.emptyQuestions }}</li> }
      </ul>
      <form class="mt-8 space-y-3 rounded-2xl border border-line bg-card p-4" (submit)="create($event)">
        <h2 class="font-serif text-xl" i18n>Single choice</h2>
        <input class="h-11 w-full rounded-lg border border-line px-3" name="prompt" required i18n-placeholder placeholder="Prompt" />
        <input class="h-11 w-full rounded-lg border border-line px-3" name="correct" required i18n-placeholder placeholder="Correct option" />
        <input class="h-11 w-full rounded-lg border border-line px-3" name="wrong" required i18n-placeholder placeholder="Other option" />
        <button class="h-11 rounded-lg bg-teal px-4 text-card" i18n>Save draft</button>
      </form>
      @if (error()) { <p class="mt-4 text-sm text-clay" role="alert">{{ error() }}</p> }
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
    this.questions.set(await this.api.get<QuestionRow[]>(`/api/assessments/v1/questions?functionId=${functionId}`));
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    this.error.set(null);
    try {
      await this.api.send('POST', '/api/assessments/v1/questions', {
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
      }, true);
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
