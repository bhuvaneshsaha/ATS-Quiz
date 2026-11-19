import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api, problemMessage } from '../../core/api';
import { SessionStore } from '../../core/session';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { typeLabel } from '../take/paper';
import { QuestionDraft, buildQuestionContent, emptyDraft } from './question-content';

interface QuestionDetail {
  id: string;
  functionId: string;
  functionName: string;
  status: string;
  type: string;
  points: number;
  content: {
    prompt?: string;
    rubric?: string;
    correctValue?: number;
    tolerance?: number;
    partialCredit?: string;
    options?: { id: string; text: string; isCorrect: boolean }[];
    items?: { id: string; text: string }[];
    pairs?: { id: string; left: string; right: string }[];
  };
}

@Component({
  selector: 'app-question-detail',
  imports: [RouterLink, Button, FieldControl, Card, FormField, PageHeader],
  template: `
    <section class="page">
      <a routerLink="/questions" i18n>Back to questions</a>
      @if (question(); as item) {
        <app-page-header>
          <ng-container pageTitle>{{ item.content.prompt }}</ng-container>
          <ng-container pageSubtitle>{{ label(item.type) }} · {{ item.points }} points · {{ item.functionName || 'Function hidden' }} · {{ item.status }}</ng-container>
        </app-page-header>
        <app-card>
          @if (item.content.options) {
            <ul class="flex flex-col gap-2">
              @for (option of item.content.options; track option.id) {
                <li>{{ option.text }}{{ option.isCorrect ? ' · correct' : '' }}</li>
              }
            </ul>
          }
          @if (item.content.correctValue !== undefined) {
            <p>Correct value {{ item.content.correctValue }}, tolerance {{ item.content.tolerance }}.</p>
          }
          @if (item.content.rubric) {
            <p>Rubric: {{ item.content.rubric }}</p>
          }
          @if (item.content.items) {
            <ol class="list-decimal pl-5">
              @for (entry of item.content.items; track entry.id) {
                <li>{{ entry.text }}</li>
              }
            </ol>
          }
          @if (item.content.pairs) {
            <ul>
              @for (pair of item.content.pairs; track pair.id) {
                <li>{{ pair.left }} → {{ pair.right }}</li>
              }
            </ul>
          }
          @if (item.content.partialCredit) {
            <p class="text-sm text-muted">Partial credit: {{ item.content.partialCredit }}</p>
          }
        </app-card>
        @if (item.status === 'Draft' && canWrite()) {
          <app-card>
            <h2 i18n>Edit draft</h2>
            <form class="flex flex-col gap-3" (submit)="save($event)">
              <app-form-field label="Prompt" [required]="true" [error]="fieldError()">
                <textarea appControl rows="3" [value]="draft().prompt" (input)="draft.update((current) => ({ ...current, prompt: $any($event.target).value }))"></textarea>
              </app-form-field>
              <app-form-field label="Points" [required]="true">
                <input appControl type="number" min="0.5" step="0.5" [value]="draft().points" (input)="draft.update((current) => ({ ...current, points: num($any($event.target).value) }))" />
              </app-form-field>
              <button appButton i18n>Save changes</button>
            </form>
          </app-card>
        }
        <div class="flex flex-wrap gap-2">
          @if (item.status === 'Draft' && canWrite()) {
            <button appButton type="button" (click)="submitReview()" i18n>Submit for review</button>
          }
          @if (item.status === 'InReview' && canApprove()) {
            @if (confirming()) {
              <p class="text-sm" role="status">Publish this question? Reviewers and tests can then use it.</p>
              <button appButton type="button" (click)="publish()" i18n>Confirm publish</button>
            } @else {
              <button appButton type="button" (click)="confirming.set(true)" i18n>Publish</button>
            }
          }
          @if (item.status === 'Published' && canWrite()) {
            <button appButton variant="secondary" type="button" (click)="revise()" i18n>New draft from this</button>
          }
        </div>
      }
      @if (notice()) {
        <p class="text-sm text-ink-soft" role="status">{{ notice() }}</p>
      }
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class QuestionDetailPage {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionStore);
  protected readonly question = signal<QuestionDetail | null>(null);
  protected readonly draft = signal<QuestionDraft>(emptyDraft());
  protected readonly error = signal<string | null>(null);
  protected readonly fieldError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly confirming = signal(false);
  protected readonly label = typeLabel;

  constructor() {
    void this.reload();
  }

  protected canWrite(): boolean {
    return this.session.can('Assessments.Questions.Write');
  }

  protected canApprove(): boolean {
    return this.session.can('Assessments.Questions.Approve');
  }

  protected num(value: string): number {
    return Number(value);
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    const item = this.question();
    if (!item) return;
    this.fieldError.set(null);
    const built = buildQuestionContent(this.merged(item));
    if ('error' in built) {
      this.fieldError.set(built.error);
      return;
    }
    try {
      await this.api.send('PUT', `/api/assessments/v1/questions/${item.id}`, {
        points: this.draft().points,
        timeLimitSeconds: null,
        skillIds: [],
        content: built.content,
      });
      this.notice.set('Draft saved.');
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async submitReview(): Promise<void> {
    await this.post('submit', 'Submitted for review.');
  }

  protected async publish(): Promise<void> {
    await this.post('publish', 'Published.');
  }

  protected async revise(): Promise<void> {
    const item = this.question();
    if (!item) return;
    const built = buildQuestionContent(this.merged(item));
    if ('error' in built) {
      this.error.set(built.error);
      return;
    }
    try {
      await this.api.send('POST', `/api/assessments/v1/questions/${item.id}/revise`, {
        points: item.points,
        timeLimitSeconds: null,
        skillIds: [],
        content: built.content,
      });
      this.notice.set('A new draft was created from the published question.');
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private merged(item: QuestionDetail): QuestionDraft {
    const base = draftFrom(item);
    return { ...base, prompt: this.draft().prompt, points: this.draft().points };
  }

  private async post(action: string, message: string): Promise<void> {
    const item = this.question();
    if (!item) return;
    this.error.set(null);
    try {
      await this.api.send('POST', `/api/assessments/v1/questions/${item.id}/${action}`, {});
      this.notice.set(message);
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private async reload(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    try {
      const item = await this.api.get<QuestionDetail>(`/api/assessments/v1/questions/${id}`);
      this.question.set(item);
      this.draft.set(draftFrom(item));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}

function draftFrom(item: QuestionDetail): QuestionDraft {
  const draft = emptyDraft();
  draft.type = item.type;
  draft.prompt = item.content.prompt ?? '';
  draft.points = item.points;
  draft.partialCredit = item.content.partialCredit ?? 'none';
  draft.rubric = item.content.rubric ?? '';
  draft.correctValue = item.content.correctValue === undefined ? '' : String(item.content.correctValue);
  draft.tolerance = item.content.tolerance === undefined ? '0' : String(item.content.tolerance);
  if (item.content.options?.length) {
    draft.options = item.content.options.map((option) => ({ id: option.id, text: option.text, correct: option.isCorrect }));
  }
  if (item.content.items?.length) {
    draft.items = item.content.items.map((entry) => entry.text);
  }
  if (item.content.pairs?.length) {
    draft.pairs = item.content.pairs.map((pair) => ({ left: pair.left, right: pair.right }));
  }
  return draft;
}
