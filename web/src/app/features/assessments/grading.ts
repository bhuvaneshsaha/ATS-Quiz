import { Component, inject, signal } from '@angular/core';
import { Api } from '../../core/api';
import { en } from '../../../locale/en';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { EmptyState } from '../../shared/components/empty-state';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { StatusPill } from '../../shared/components/status-pill';

interface GradeItem {
  answerId: string;
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
        <ng-container pageSubtitle i18n
          >Names are hidden. Score the answer in front of you.</ng-container
        >
      </app-page-header>
      @for (item of queue(); track item.answerId) {
        <app-card>
          <div class="flex flex-wrap items-center justify-between gap-3">
            <h2>{{ item.prompt }}</h2>
            <app-status-pill status="Needs grading" />
          </div>
          <p class="rounded-control bg-canvas px-4 py-3.5">{{ item.responseJson }}</p>
          <p class="text-sm text-muted">{{ item.rubric }}</p>
          <form class="flex flex-wrap items-end gap-3" (submit)="grade(item, $event)">
            <app-form-field label="Score" [required]="true">
              <input
                appControl
                class="w-24"
                name="score"
                type="number"
                min="0"
                [max]="item.points"
                required
              />
            </app-form-field>
            <button appButton i18n>Save score</button>
          </form>
        </app-card>
      } @empty {
        <app-empty-state [message]="copy.emptyGrading" />
      }
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
    await this.api.send('POST', `/api/assessments/v1/grading/answers/${item.answerId}`, {
      score,
      comment: null,
    });
    await this.reload();
  }
}
