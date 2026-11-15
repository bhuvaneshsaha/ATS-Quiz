import { Component, inject, signal } from '@angular/core';
import { Api } from '../../core/api';
import { en } from '../../../locale/en';

interface GradeItem {
  answerId: string;
  prompt: string;
  rubric: string;
  points: number;
  responseJson: string;
}

@Component({
  selector: 'app-grading',
  template: `
    <section class="mx-auto max-w-3xl px-4 py-6">
      <h1 class="font-serif text-3xl" i18n>Grading</h1>
      <p class="mt-2 text-sm text-ink/70" i18n>Names are hidden. Score the answer in front of you.</p>
      <div class="mt-6 space-y-4">
        @for (item of queue(); track item.answerId) {
          <article class="rounded-2xl border border-line bg-card p-4">
            <p class="font-medium">{{ item.prompt }}</p>
            <p class="mt-2 text-sm">{{ item.responseJson }}</p>
            <p class="mt-2 text-sm text-ink/70">{{ item.rubric }}</p>
            <form class="mt-3 flex gap-2" (submit)="grade(item, $event)">
              <input class="h-11 w-24 rounded-lg border border-line px-3" name="score" type="number" min="0" [max]="item.points" required />
              <button class="h-11 rounded-lg bg-teal px-4 text-card" i18n>Save score</button>
            </form>
          </article>
        } @empty { <p class="text-sm text-ink/70">{{ copy.emptyGrading }}</p> }
      </div>
    </section>
  `,
})
export class GradingPage {
  private readonly api = inject(Api);
  protected readonly copy = en;
  protected readonly queue = signal<GradeItem[]>([]);

  constructor() {
    void this.reload();
  }

  private async reload(): Promise<void> {
    this.queue.set(await this.api.get<GradeItem[]>('/api/assessments/v1/grading/queue'));
  }

  protected async grade(item: GradeItem, event: Event): Promise<void> {
    event.preventDefault();
    const score = Number(new FormData(event.target as HTMLFormElement).get('score'));
    await this.api.send('POST', `/api/assessments/v1/grading/answers/${item.answerId}`, { score, comment: null });
    await this.reload();
  }
}
