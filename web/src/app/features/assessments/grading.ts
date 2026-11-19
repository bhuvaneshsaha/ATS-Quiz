import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';
import { readableAnswer } from '../take/paper';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { EmptyState } from '../../shared/components/empty-state';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { StatusPill } from '../../shared/components/status-pill';

interface GradeItem {
  answerId: string;
  assessmentName?: string;
  prompt: string;
  rubric: string;
  points: number;
  responseJson: string;
}

@Component({
  selector: 'app-grading',
  imports: [Button, FieldControl, Card, EmptyState, FormField, PageHeader, StatusPill],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Grading</ng-container>
        <ng-container pageSubtitle i18n>Names are hidden. Score the answer in front of you.</ng-container>
      </app-page-header>
      @if (notice()) {
        <p class="text-sm text-ink-soft" role="status">{{ notice() }}</p>
      }
      @for (item of queue(); track item.answerId) {
        <app-card>
          <div class="flex flex-wrap items-center justify-between gap-3">
            <div>
              @if (item.assessmentName) {
                <p class="text-sm text-muted">{{ item.assessmentName }}</p>
              }
              <h2>{{ item.prompt }}</h2>
            </div>
            <app-status-pill status="Needs grading" />
          </div>
          <p class="rounded-control bg-canvas px-4 py-3.5 whitespace-pre-wrap">{{ answer(item) }}</p>
          <p class="text-sm text-muted">{{ item.rubric }}</p>
          <form class="flex flex-col gap-3" (submit)="grade(item, $event)">
            <div class="flex flex-wrap items-end gap-3">
              <app-form-field [label]="'Score out of ' + item.points" [required]="true">
                <input appControl class="w-24" name="score" type="number" min="0" [max]="item.points" step="0.5" required />
              </app-form-field>
              <app-form-field class="min-w-60 flex-1" label="Comment">
                <textarea appControl name="comment" rows="3"></textarea>
              </app-form-field>
              @if (confirming() === item.answerId) {
                <p class="text-sm" role="status">Save this score? The card leaves the queue.</p>
                <button appButton i18n>Confirm score</button>
              } @else {
                <button appButton type="button" (click)="confirming.set(item.answerId)" i18n>Save score</button>
              }
            </div>
          </form>
        </app-card>
      } @empty {
        <app-empty-state [message]="copy.emptyGrading" />
      }
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class GradingPage {
  private readonly api = inject(Api);
  protected readonly copy = en;
  protected readonly queue = signal<GradeItem[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly confirming = signal<string | null>(null);

  constructor() {
    void this.reload();
  }

  protected answer(item: GradeItem): string {
    return readableAnswer(item.responseJson);
  }

  protected async grade(item: GradeItem, event: Event): Promise<void> {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    const score = Number(data.get('score'));
    const comment = data.get('comment')?.toString().trim() || null;
    this.error.set(null);
    this.notice.set(null);
    try {
      await this.api.send('POST', `/api/assessments/v1/grading/answers/${item.answerId}`, { score, comment });
      this.notice.set(`Saved ${score} out of ${item.points}.`);
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private async reload(): Promise<void> {
    try {
      this.queue.set(await this.api.get<GradeItem[]>('/api/assessments/v1/grading/queue'));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
