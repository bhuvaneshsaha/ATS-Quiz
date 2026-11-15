import { NgTemplateOutlet } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { BrnSheetImports } from '@spartan-ng/brain/sheet';
import { firstValueFrom, timer } from 'rxjs';
import { Api, problemMessage } from '../../core/api';
import { backoffDelay, shouldRetrySave } from '../../shared/answer-queue';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { ConfirmDialog } from '../../shared/components/confirm-dialog';
import { ProgressBar } from '../../shared/components/progress-bar';

interface Option {
  id: string;
  text: string;
}
interface Presented {
  questionVersionId: string;
  type: string;
  prompt: string;
  options: Option[];
}
interface Section {
  sectionId: string;
  name: string;
  questions: Presented[];
}
interface Paper {
  attemptId: string;
  deadline: string;
  sections: Section[];
}
interface Brand {
  accent: string;
  accentStrong: string;
}

@Component({
  selector: 'app-take',
  imports: [
    NgTemplateOutlet,
    BrnSheetImports,
    Button,
    FieldControl,
    Card,
    ConfirmDialog,
    ProgressBar,
  ],
  host: {
    class: 'take-area block min-h-dvh bg-canvas text-ink',
    '[style.--color-accent]': 'brand()?.accent',
    '[style.--color-accent-strong]': 'brand()?.accentStrong',
  },
  template: `
    <div class="flex min-h-dvh flex-col">
      <header
        class="flex flex-wrap items-center justify-between gap-3 border-b border-line bg-surface px-4 py-3 lg:px-8"
      >
        <div>
          <p class="font-display text-lg font-semibold">Shortlist</p>
          <p class="text-sm text-muted">
            @if (step() === 'player') {
              Question {{ cursor() + 1 }} of {{ flat().length }}
            } @else {
              Assessment
            }
          </p>
        </div>
        @if (step() === 'player') {
          <div class="flex flex-wrap items-center gap-4">
            <p class="text-sm text-muted">{{ saveState() }}</p>
            <p
              class="rounded-control bg-ink px-3.5 py-2 font-display text-lg font-semibold text-white tabular-nums"
              role="timer"
            >
              {{ remaining() }}
            </p>
          </div>
        }
      </header>
      @if (step() === 'player') {
        <app-progress-bar [value]="answeredCount()" [max]="flat().length || 1" />
      }

      <div class="take-wrap flex flex-1 flex-col gap-5">
        @switch (step()) {
          @case ('welcome') {
            <div class="flex flex-col gap-2">
              <p class="text-muted">Invitation</p>
              <h1 class="text-3xl" i18n>Your assessment</h1>
              <p class="max-w-xl text-ink-soft" i18n>
                We will email a one-time code before the timer starts. Find a quiet place; once you
                start, the clock is kept by the server.
              </p>
            </div>
            <button appButton class="self-start" type="button" (click)="requestCode()" i18n>
              Email me a code
            </button>
          }
          @case ('code') {
            <h1 class="text-3xl" i18n>Enter your code</h1>
            <app-card>
              <form class="flex flex-col gap-4" (submit)="verify($event)">
                <label class="flex flex-col gap-1 text-sm text-muted">
                  <span i18n>One-time code (required)</span>
                  <input
                    appControl
                    name="code"
                    inputmode="numeric"
                    autocomplete="one-time-code"
                    required
                  />
                </label>
                <button appButton class="self-start" i18n>Continue</button>
              </form>
            </app-card>
          }
          @case ('check') {
            <h1 class="text-3xl" i18n>Before you start</h1>
            <app-card>
              <h2 class="font-display text-xl">System check</h2>
              @if (narrow()) {
                <p class="text-ink-soft" i18n>
                  This test is comfortable on a phone. The question list opens from a button at the
                  bottom.
                </p>
              } @else {
                <p class="text-ink-soft" i18n>
                  Your screen is wide enough. The timer is kept by the server.
                </p>
              }
              <button appButton class="self-start" type="button" (click)="start()" i18n>
                Start assessment
              </button>
            </app-card>
          }
          @case ('player') {
            @if (current(); as question) {
              <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
                <main class="flex min-w-0 flex-1 flex-col gap-5">
                  <app-card>
                    <div class="flex flex-wrap items-center justify-between gap-3">
                      <p class="font-semibold">
                        Question {{ cursor() + 1 }} of {{ flat().length }}
                      </p>
                      <p class="text-sm text-muted">{{ question.type }}</p>
                    </div>
                    <h1 class="font-display text-xl font-medium">{{ question.prompt }}</h1>
                    <div class="flex flex-col gap-2.5">
                      @for (option of question.options; track option.id) {
                        <button
                          class="min-h-11 rounded-control border-2 px-4 py-3 text-left transition-colors duration-150"
                          [class.border-accent]="chosen(question.questionVersionId) === option.id"
                          [class.bg-accent-tint]="chosen(question.questionVersionId) === option.id"
                          [class.border-line]="chosen(question.questionVersionId) !== option.id"
                          [class.bg-surface]="chosen(question.questionVersionId) !== option.id"
                          type="button"
                          (click)="choose(question, option.id)"
                        >
                          {{ option.text }}
                        </button>
                      }
                    </div>
                    <button
                      appButton
                      variant="secondary"
                      type="button"
                      (click)="toggleFlag(question.questionVersionId)"
                    >
                      {{ flagged(question.questionVersionId) ? 'Flagged' : 'Flag for review' }}
                    </button>
                  </app-card>
                  <div class="flex flex-wrap justify-between gap-3">
                    <button
                      appButton
                      variant="secondary"
                      type="button"
                      [disabled]="cursor() === 0"
                      (click)="cursor.set(cursor() - 1)"
                      i18n
                    >
                      Previous
                    </button>
                    <button
                      appButton
                      type="button"
                      [disabled]="cursor() >= flat().length - 1"
                      (click)="cursor.set(cursor() + 1)"
                      i18n
                    >
                      Next
                    </button>
                  </div>
                  <app-confirm-dialog
                    triggerLabel="Review and submit"
                    triggerVariant="dark"
                    title="Submit this assessment?"
                    body="You cannot change answers after submitting."
                    confirmLabel="Submit"
                    (confirmed)="submit()"
                  />
                </main>
                <aside
                  class="hidden w-full flex-col gap-4 rounded-panel border border-line bg-surface p-5 md:flex lg:w-80"
                >
                  <ng-container *ngTemplateOutlet="navigator" />
                </aside>
                <div class="md:hidden" brnSheet side="bottom">
                  <button
                    appButton
                    brnSheetTrigger
                    variant="secondary"
                    class="w-full"
                    type="button"
                    i18n
                  >
                    Questions
                  </button>
                  <div brnSheetOverlay class="bg-ink/40"></div>
                  <ng-template brnSheetContent>
                    <div class="take-sheet flex flex-col gap-4 rounded-t-panel bg-surface p-4">
                      <ng-container *ngTemplateOutlet="navigator" />
                    </div>
                  </ng-template>
                </div>
              </div>
            }
          }
          @case ('done') {
            <h1 class="text-3xl" i18n>Submitted</h1>
            <p class="max-w-xl text-ink-soft" i18n>
              Your answers are with the hiring team. You can close this page.
            </p>
          }
        }
        @if (error()) {
          <p class="text-sm text-danger" role="alert">{{ error() }}</p>
        }
      </div>
    </div>

    <ng-template #navigator>
      <div class="flex items-baseline justify-between gap-3">
        <p class="font-semibold">Questions</p>
        <p class="text-sm text-muted">{{ answeredCount() }} of {{ flat().length }} answered</p>
      </div>
      <div class="grid grid-cols-6 gap-1.5">
        @for (question of flat(); track question.questionVersionId; let position = $index) {
          <button
            class="h-11 rounded-control border-2 text-sm font-semibold"
            [class.border-ink]="cursor() === position"
            [class.bg-accent]="answered(question.questionVersionId)"
            [class.text-white]="answered(question.questionVersionId)"
            [class.border-accent]="answered(question.questionVersionId) && cursor() !== position"
            [class.bg-warn-tint]="
              flagged(question.questionVersionId) && !answered(question.questionVersionId)
            "
            [class.border-warn-line]="
              flagged(question.questionVersionId) &&
              !answered(question.questionVersionId) &&
              cursor() !== position
            "
            [class.bg-surface]="
              cursor() === position &&
              !answered(question.questionVersionId) &&
              !flagged(question.questionVersionId)
            "
            [class.border-transparent]="
              cursor() !== position &&
              !answered(question.questionVersionId) &&
              !flagged(question.questionVersionId)
            "
            [class.bg-line-soft]="
              cursor() !== position &&
              !answered(question.questionVersionId) &&
              !flagged(question.questionVersionId)
            "
            type="button"
            (click)="cursor.set(position)"
          >
            {{ position + 1 }}
          </button>
        }
      </div>
    </ng-template>
  `,
})
export class TakePage {
  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  protected readonly step = signal<'welcome' | 'code' | 'check' | 'player' | 'done'>('welcome');
  protected readonly error = signal<string | null>(null);
  protected readonly paper = signal<Paper | null>(null);
  protected readonly cursor = signal(0);
  protected readonly saveState = signal('Saved on this device until the server confirms.');
  protected readonly narrow = signal(window.matchMedia('(max-width: 767px)').matches);
  protected readonly brand = signal<Brand | null>(null);
  protected readonly answers = signal<Record<string, string>>({});
  protected readonly flags = signal<ReadonlySet<string>>(new Set());
  private readonly now = signal(Date.now());
  private readonly sequences = new Map<string, number>();
  private readonly token = this.route.snapshot.paramMap.get('token') ?? '';

  protected readonly flat = computed(
    () =>
      this.paper()?.sections.flatMap((section) =>
        section.questions.map((question) => ({ ...question, sectionId: section.sectionId })),
      ) ?? [],
  );
  protected readonly current = computed(() => this.flat()[this.cursor()] ?? null);
  protected readonly answeredCount = computed(() => Object.keys(this.answers()).length);
  protected readonly remaining = computed(() => {
    const deadline = this.paper()?.deadline;
    if (!deadline) {
      return '';
    }
    const seconds = Math.max(0, Math.round((new Date(deadline).getTime() - this.now()) / 1000));
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

  constructor() {
    const handle = window.setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => window.clearInterval(handle));
  }

  protected chosen(id: string): string | undefined {
    return this.answers()[id];
  }

  protected answered(id: string): boolean {
    return this.answers()[id] !== undefined;
  }

  protected flagged(id: string): boolean {
    return this.flags().has(id);
  }

  protected toggleFlag(id: string): void {
    const next = new Set(this.flags());
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    this.flags.set(next);
  }

  protected async requestCode(): Promise<void> {
    this.error.set(null);
    try {
      await this.api.send('POST', `/api/assessments/v1/take/${this.token}/code`, {});
      this.step.set('code');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async verify(event: Event): Promise<void> {
    event.preventDefault();
    const code = new FormData(event.target as HTMLFormElement).get('code');
    try {
      await this.api.send('POST', `/api/assessments/v1/take/${this.token}/verify`, { code });
      this.step.set('check');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async start(): Promise<void> {
    try {
      this.paper.set(await this.api.send<Paper>('POST', '/api/assessments/v1/take/start', {}));
      this.step.set('player');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected choose(question: Presented & { sectionId: string }, optionId: string): void {
    this.answers.update((current) => ({ ...current, [question.questionVersionId]: optionId }));
    const sequence = (this.sequences.get(question.questionVersionId) ?? 0) + 1;
    this.sequences.set(question.questionVersionId, sequence);
    void this.save(question, optionId, sequence, 1);
  }

  private async save(
    question: Presented & { sectionId: string },
    optionId: string,
    sequence: number,
    attempt: number,
  ): Promise<void> {
    const paper = this.paper();
    if (!paper) {
      return;
    }
    this.saveState.set('Saving…');
    try {
      const response = await firstValueFrom(
        this.http.post<{ accepted: boolean; deadline: string }>(
          '/api/assessments/v1/take/answers',
          {
            attemptId: paper.attemptId,
            questionVersionId: question.questionVersionId,
            sectionId: question.sectionId,
            sequence,
            response: { optionId },
          },
        ),
      );
      this.paper.update((current) =>
        current ? { ...current, deadline: response.deadline } : current,
      );
      this.saveState.set(response.accepted ? 'Saved' : 'Kept the newer answer');
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (shouldRetrySave(status) && attempt < 6) {
        await firstValueFrom(timer(backoffDelay(attempt)));
        await this.save(question, optionId, sequence, attempt + 1);
        return;
      }
      this.saveState.set(problemMessage(error));
    }
  }

  protected async submit(): Promise<void> {
    const paper = this.paper();
    if (!paper) {
      return;
    }
    try {
      await this.api.send('POST', '/api/assessments/v1/take/submit', {
        attemptId: paper.attemptId,
      });
      this.step.set('done');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
