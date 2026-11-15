import { NgTemplateOutlet } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { BrnSheetImports } from '@spartan-ng/brain/sheet';
import { firstValueFrom, timer } from 'rxjs';
import { Api, problemMessage } from '../../core/api';
import { backoffDelay, shouldRetrySave } from '../../shared/answer-queue';
import { AnswerValue, answerFromResponse, responseFor, typeLabel } from './paper';
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
  items: Option[];
  left: Option[];
  right: Option[];
}
interface Section {
  sectionId: string;
  name: string;
  minutes: number;
  deadline: string;
  questions: Presented[];
}
interface Paper {
  attemptId: string;
  deadline: string;
  desktopOnly: boolean;
  sections: Section[];
  answers: { questionVersionId: string; response: unknown }[];
}
interface Preview {
  invitationId: string;
  assessmentName: string;
  desktopOnly: boolean;
  totalMinutes: number;
  extraTimePercent: number;
  enforcedMinutes: number;
  attemptStarted: boolean;
  attemptFinished: boolean;
}
interface FlatQuestion extends Presented {
  sectionId: string;
}

@Component({
  selector: 'app-take',
  imports: [NgTemplateOutlet, BrnSheetImports, Button, FieldControl, Card, ConfirmDialog, ProgressBar],
  host: { class: 'take-area block min-h-dvh bg-canvas text-ink' },
  template: `
    <div class="flex min-h-dvh flex-col">
      <header class="flex flex-wrap items-center justify-between gap-3 border-b border-line bg-surface px-4 py-3 lg:px-8">
        <div>
          <p class="font-display text-lg font-semibold">Shortlist</p>
          <p class="text-sm text-muted">{{ preview()?.assessmentName || 'Assessment' }}</p>
        </div>
        @if (step() === 'player') {
          <div class="flex flex-wrap items-center gap-4">
            <p class="text-sm text-muted" role="status">{{ saveState() }}</p>
            <div class="text-right">
              <p class="text-xs text-muted">{{ currentSectionName() }}</p>
              <p class="rounded-control bg-ink px-3.5 py-2 font-display text-lg font-semibold text-white tabular-nums" role="timer" aria-live="polite" [attr.aria-label]="'Time remaining ' + remaining()">
                {{ remaining() }}
              </p>
            </div>
          </div>
        }
      </header>
      @if (step() === 'player') {
        <app-progress-bar size="line" [label]="'Answered ' + answeredCount() + ' of ' + flat().length" [value]="answeredCount()" [max]="flat().length || 1" />
      }

      <div class="take-wrap flex flex-1 flex-col gap-5">
        @switch (step()) {
          @case ('welcome') {
            <div class="flex flex-col gap-2">
              <p class="text-muted">Invitation</p>
              <h1 class="text-3xl" i18n>Your assessment</h1>
              <p class="max-w-xl text-ink-soft" i18n>
                We will email a one-time code before the timer starts. Once you start, the clock is kept by the server.
              </p>
            </div>
            <button appButton class="self-start" type="button" (click)="requestCode()" i18n>Email me a code</button>
          }
          @case ('code') {
            <h1 class="text-3xl" i18n>Enter your code</h1>
            <app-card>
              <form class="flex flex-col gap-4" (submit)="verify($event)">
                <label class="flex flex-col gap-1 text-sm text-muted">
                  <span i18n>One-time code (required)</span>
                  <input appControl name="code" inputmode="numeric" autocomplete="one-time-code" required />
                </label>
                <button appButton class="self-start" i18n>Continue</button>
              </form>
            </app-card>
          }
          @case ('check') {
            <h1 class="text-3xl" i18n>Before you start</h1>
            <app-card>
              <h2 class="font-display text-xl">{{ preview()?.assessmentName || 'System check' }}</h2>
              <p class="text-ink-soft">{{ durationText() }}</p>
              @if (blockedDesktop()) {
                <p class="text-ink-soft" i18n>This assessment has to be started on a screen at least 1024 pixels wide.</p>
              } @else if (phone()) {
                <p class="text-ink-soft" i18n>This test is comfortable on a phone. The question list opens from a button at the bottom.</p>
              }
              @if (!blockedDesktop()) {
                <button appButton class="self-start" type="button" (click)="start()" i18n>Start assessment</button>
              }
            </app-card>
          }
          @case ('player') {
            @if (current(); as question) {
              <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
                <main class="flex min-w-0 flex-1 flex-col gap-5">
                  <app-card tone="panel">
                    <div class="flex flex-wrap items-center justify-between gap-3">
                      <p class="font-semibold">Question {{ cursor() + 1 }} of {{ flat().length }}</p>
                      <p class="text-sm text-muted">{{ label(question.type) }}</p>
                    </div>
                    <h1 class="font-display text-xl font-medium">{{ question.prompt }}</h1>
                    @if (sectionClosed(question)) {
                      <p class="text-sm text-danger" role="alert" i18n>Time for this section has ended.</p>
                    } @else {
                      @switch (question.type) {
                        @case ('single_choice') {
                          <fieldset class="flex flex-col gap-2.5 border-0 p-0">
                            <legend class="pb-2.5 text-sm text-muted" i18n>Choose one answer</legend>
                            @for (option of question.options; track option.id) {
                              <label class="flex min-h-11 cursor-pointer items-start gap-3 rounded-control border-2 px-4 py-3.5" [class.border-accent]="single(question) === option.id" [class.border-line]="single(question) !== option.id">
                                <input class="mt-0.5 size-5" type="radio" [name]="question.questionVersionId" [checked]="single(question) === option.id" (change)="chooseSingle(question, option.id)" />
                                <span>{{ option.text }}</span>
                              </label>
                            }
                          </fieldset>
                        }
                        @case ('multiple_choice') {
                          <fieldset class="flex flex-col gap-2.5 border-0 p-0">
                            <legend class="pb-2.5 text-sm text-muted" i18n>Choose every answer that applies</legend>
                            @for (option of question.options; track option.id) {
                              <label class="flex min-h-11 cursor-pointer items-start gap-3 rounded-control border-2 px-4 py-3.5" [class.border-accent]="checked(question, option.id)" [class.border-line]="!checked(question, option.id)">
                                <input class="mt-0.5 size-5" type="checkbox" [checked]="checked(question, option.id)" (change)="toggleMany(question, option.id, $any($event.target).checked)" />
                                <span>{{ option.text }}</span>
                              </label>
                            }
                          </fieldset>
                        }
                        @case ('numeric') {
                          <label class="flex flex-col gap-1 text-sm text-muted">
                            <span i18n>Your number</span>
                            <input appControl type="number" step="any" [value]="numeric(question)" (input)="setNumeric(question, $any($event.target).value)" />
                          </label>
                        }
                        @case ('long_answer') {
                          <label class="flex flex-col gap-1 text-sm text-muted">
                            <span i18n>Your answer</span>
                            <textarea appControl rows="8" [value]="text(question)" (input)="setText(question, $any($event.target).value)"></textarea>
                          </label>
                        }
                        @case ('ordering') {
                          <p class="text-sm text-muted" i18n>Put these in the correct order, then keep it.</p>
                          <button appButton variant="secondary" type="button" (click)="keepOrder(question)">Keep this order</button>
                          <ol class="flex flex-col gap-2">
                            @for (id of order(question); track id; let index = $index) {
                              <li class="flex items-center gap-2">
                                <span class="flex-1 rounded-control border border-line px-3 py-2">{{ itemText(question, id) }}</span>
                                <button appButton variant="secondary" type="button" [disabled]="index === 0" (click)="moveItem(question, index, -1)">Up</button>
                                <button appButton variant="secondary" type="button" [disabled]="index === order(question).length - 1" (click)="moveItem(question, index, 1)">Down</button>
                              </li>
                            }
                          </ol>
                        }
                        @case ('matching') {
                          <div class="flex flex-col gap-2">
                            @for (left of question.left; track left.id) {
                              <label class="flex flex-col gap-1 text-sm">
                                <span>{{ left.text }}</span>
                                <select appControl [value]="matched(question, left.id)" (change)="setMatch(question, left.id, $any($event.target).value)">
                                  <option value="">Select</option>
                                  @for (right of question.right; track right.id) {
                                    <option [value]="right.id">{{ right.text }}</option>
                                  }
                                </select>
                              </label>
                            }
                          </div>
                        }
                      }
                    }
                    <button appButton variant="secondary" type="button" (click)="toggleFlag(question.questionVersionId)">
                      {{ flagged(question.questionVersionId) ? 'Flagged' : 'Flag for review' }}
                    </button>
                  </app-card>
                  <div class="flex flex-wrap justify-between gap-3">
                    <button appButton variant="secondary" type="button" [disabled]="cursor() === 0" (click)="cursor.set(cursor() - 1)" i18n>Previous</button>
                    <button appButton type="button" [disabled]="cursor() >= flat().length - 1" (click)="cursor.set(cursor() + 1)" i18n>Next</button>
                  </div>
                  <app-confirm-dialog
                    triggerLabel="Review and submit"
                    triggerVariant="dark"
                    title="Submit this assessment?"
                    [body]="submitBody()"
                    confirmLabel="Submit"
                    (confirmed)="submit(false)"
                  />
                </main>
                <aside class="hidden w-full flex-col gap-4 rounded-panel border border-line bg-surface p-5 md:flex lg:w-80">
                  <ng-container *ngTemplateOutlet="navigator" />
                </aside>
                <div class="md:hidden" brnSheet side="bottom">
                  <button appButton brnSheetTrigger variant="secondary" class="w-full" type="button" i18n>Questions</button>
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
            <h1 class="text-3xl">{{ timedOut() ? 'Time is up' : 'Your assessment is in' }}</h1>
            <p class="max-w-xl text-ink-soft">
              {{ timedOut() ? 'Your saved answers were submitted.' : 'Your answers were submitted. You can close this page.' }}
            </p>
            @if (paper()?.attemptId) {
              <p class="text-sm text-muted">Reference {{ paper()?.attemptId }}</p>
            }
          }
          @case ('already') {
            <h1 class="text-3xl" i18n>You already submitted this assessment.</h1>
            <p class="max-w-xl text-ink-soft" i18n>Ask the recruiter if you need another attempt.</p>
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
          <button class="h-11 rounded-control border-2 text-sm font-semibold" type="button" [attr.aria-label]="'Question ' + (position + 1)" [class.border-ink]="cursor() === position" [class.bg-accent]="answered(question)" [class.text-white]="answered(question)" (click)="cursor.set(position)">
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
  protected readonly step = signal<'welcome' | 'code' | 'check' | 'player' | 'done' | 'already'>('welcome');
  protected readonly error = signal<string | null>(null);
  protected readonly paper = signal<Paper | null>(null);
  protected readonly preview = signal<Preview | null>(null);
  protected readonly cursor = signal(0);
  protected readonly saveState = signal('Answers are saved when the server confirms.');
  protected readonly phone = signal(window.matchMedia('(max-width: 767px)').matches);
  protected readonly compact = signal(window.matchMedia('(max-width: 1023px)').matches);
  protected readonly answers = signal<Record<string, AnswerValue>>({});
  protected readonly flags = signal<ReadonlySet<string>>(new Set());
  protected readonly timedOut = signal(false);
  private readonly now = signal(Date.now());
  private readonly sequences = new Map<string, number>();
  private finishing = false;
  private readonly token = this.route.snapshot.paramMap.get('token') ?? '';

  protected readonly flat = computed<FlatQuestion[]>(
    () =>
      this.paper()?.sections.flatMap((section) =>
        section.questions.map((question) => ({ ...question, sectionId: section.sectionId })),
      ) ?? [],
  );
  protected readonly current = computed(() => this.flat()[this.cursor()] ?? null);
  protected readonly answeredCount = computed(
    () => this.flat().filter((question) => responseFor(question.type, this.answers()[question.questionVersionId] ?? {})).length,
  );
  protected readonly remaining = computed(() => {
    const question = this.current();
    const section = this.paper()?.sections.find((item) => item.sectionId === question?.sectionId);
    return this.formatRemaining(section?.deadline ?? this.paper()?.deadline);
  });
  protected readonly currentSectionName = computed(() => {
    const question = this.current();
    return this.paper()?.sections.find((section) => section.sectionId === question?.sectionId)?.name ?? '';
  });
  protected readonly blockedDesktop = computed(
    () => !!this.preview()?.desktopOnly && this.compact() && !this.preview()?.attemptStarted && !this.preview()?.attemptFinished,
  );
  protected readonly durationText = computed(() => {
    const preview = this.preview();
    const minutes = preview?.enforcedMinutes || preview?.totalMinutes || 0;
    if (preview?.extraTimePercent) {
      return `${minutes} minutes, including ${preview.extraTimePercent}% extra time.`;
    }
    return `${minutes} minutes, kept by the server.`;
  });
  protected readonly submitBody = computed(() => {
    const missing = this.flat()
      .map((question, index) => (this.answered(question) ? null : index + 1))
      .filter((index): index is number => index !== null);
    const gap = missing.length ? ` Not answered: ${missing.join(', ')}.` : '';
    return `${this.answeredCount()} of ${this.flat().length} answered.${gap} You cannot change answers after submitting.`;
  });
  protected readonly label = typeLabel;

  constructor() {
    const handle = window.setInterval(() => this.tick(), 1000);
    const phone = window.matchMedia('(max-width: 767px)');
    const compact = window.matchMedia('(max-width: 1023px)');
    const apply = () => {
      this.phone.set(phone.matches);
      this.compact.set(compact.matches);
    };
    apply();
    phone.addEventListener('change', apply);
    compact.addEventListener('change', apply);
    inject(DestroyRef).onDestroy(() => {
      window.clearInterval(handle);
      phone.removeEventListener('change', apply);
      compact.removeEventListener('change', apply);
    });
    void this.restore();
  }

  protected sectionClosed(question: FlatQuestion): boolean {
    const section = this.paper()?.sections.find((item) => item.sectionId === question.sectionId);
    return !!section && new Date(section.deadline).getTime() <= this.now();
  }

  protected answered(question: FlatQuestion): boolean {
    return responseFor(question.type, this.answers()[question.questionVersionId] ?? {}) !== null;
  }

  protected flagged(id: string): boolean {
    return this.flags().has(id);
  }

  protected toggleFlag(id: string): void {
    const next = new Set(this.flags());
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.flags.set(next);
    const attemptId = this.paper()?.attemptId;
    if (attemptId) sessionStorage.setItem(`shortlist.flags.${attemptId}`, JSON.stringify([...next]));
  }

  protected single(question: FlatQuestion): string {
    return this.answers()[question.questionVersionId]?.optionId ?? '';
  }

  protected checked(question: FlatQuestion, optionId: string): boolean {
    return this.answers()[question.questionVersionId]?.optionIds?.includes(optionId) ?? false;
  }

  protected numeric(question: FlatQuestion): string {
    const value = this.answers()[question.questionVersionId]?.value;
    return value === undefined ? '' : String(value);
  }

  protected text(question: FlatQuestion): string {
    return this.answers()[question.questionVersionId]?.text ?? '';
  }

  protected order(question: FlatQuestion): string[] {
    return this.answers()[question.questionVersionId]?.itemIds ?? question.items.map((item) => item.id);
  }

  protected itemText(question: FlatQuestion, id: string): string {
    return question.items.find((item) => item.id === id)?.text ?? '';
  }

  protected matched(question: FlatQuestion, leftId: string): string {
    return this.answers()[question.questionVersionId]?.pairs?.find((pair) => pair.leftId === leftId)?.rightId ?? '';
  }

  protected chooseSingle(question: FlatQuestion, optionId: string): void {
    this.store(question, { optionId });
  }

  protected toggleMany(question: FlatQuestion, optionId: string, checked: boolean): void {
    const current = new Set(this.answers()[question.questionVersionId]?.optionIds ?? []);
    if (checked) current.add(optionId);
    else current.delete(optionId);
    this.store(question, { optionIds: [...current] });
  }

  protected setNumeric(question: FlatQuestion, raw: string): void {
    this.store(question, { value: raw === '' ? undefined : Number(raw) });
  }

  protected setText(question: FlatQuestion, text: string): void {
    this.store(question, { text });
  }

  protected keepOrder(question: FlatQuestion): void {
    this.store(question, { itemIds: this.order(question) });
  }

  protected moveItem(question: FlatQuestion, index: number, delta: number): void {
    const itemIds = [...this.order(question)];
    const next = index + delta;
    if (next < 0 || next >= itemIds.length) return;
    const [item] = itemIds.splice(index, 1);
    itemIds.splice(next, 0, item);
    this.store(question, { itemIds });
  }

  protected setMatch(question: FlatQuestion, leftId: string, rightId: string): void {
    const pairs = (this.answers()[question.questionVersionId]?.pairs ?? []).filter((pair) => pair.leftId !== leftId);
    if (rightId) pairs.push({ leftId, rightId });
    this.store(question, { pairs });
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
    this.error.set(null);
    const code = new FormData(event.target as HTMLFormElement).get('code');
    try {
      const session = await this.api.send<Preview>('POST', `/api/assessments/v1/take/${this.token}/verify`, { code });
      this.remember(session);
      this.step.set(session.attemptFinished ? 'already' : 'check');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async start(): Promise<void> {
    this.error.set(null);
    if (this.blockedDesktop()) {
      this.error.set('This assessment can only be started on a wider screen.');
      return;
    }
    try {
      this.applyPaper(await this.api.send<Paper>('POST', '/api/assessments/v1/take/start', {}));
      this.step.set('player');
    } catch (error) {
      const message = problemMessage(error);
      if (/finished|closed/i.test(message)) {
        this.step.set('already');
        return;
      }
      this.error.set(message);
    }
  }

  protected async submit(automatic: boolean): Promise<void> {
    const paper = this.paper();
    if (!paper || this.finishing) return;
    this.finishing = true;
    this.error.set(null);
    try {
      await this.api.send('POST', '/api/assessments/v1/take/submit', { attemptId: paper.attemptId });
      this.timedOut.set(automatic);
      this.step.set('done');
    } catch (error) {
      const message = problemMessage(error);
      if (/finished|closed/i.test(message)) {
        this.timedOut.set(automatic);
        this.step.set('done');
        return;
      }
      this.finishing = false;
      this.error.set(message);
    }
  }

  private async restore(): Promise<void> {
    const saved = sessionStorage.getItem(this.storageKey());
    if (!saved) return;
    try {
      const session = await this.api.get<Preview>('/api/assessments/v1/take/session');
      if (session.invitationId !== saved) return;
      this.preview.set(session);
      if (session.attemptFinished) {
        this.step.set('already');
        return;
      }
      if (session.attemptStarted) await this.start();
      else this.step.set('check');
    } catch {
      this.step.set('welcome');
    }
  }

  private remember(session: Preview): void {
    this.preview.set(session);
    sessionStorage.setItem(this.storageKey(), session.invitationId);
  }

  private applyPaper(paper: Paper): void {
    const answers: Record<string, AnswerValue> = {};
    for (const answer of paper.answers ?? []) {
      answers[answer.questionVersionId] = answerFromResponse(answer.response);
    }
    this.answers.set(answers);
    this.paper.set(paper);
    const stored = sessionStorage.getItem(`shortlist.flags.${paper.attemptId}`);
    if (stored) {
      try {
        this.flags.set(new Set(JSON.parse(stored) as string[]));
      } catch {
        this.flags.set(new Set());
      }
    }
    if (Object.keys(answers).length) {
      this.saveState.set('Saved answers restored.');
    }
  }

  private store(question: FlatQuestion, value: AnswerValue): void {
    if (this.sectionClosed(question)) return;
    this.answers.update((current) => ({ ...current, [question.questionVersionId]: value }));
    const sequence = (this.sequences.get(question.questionVersionId) ?? 0) + 1;
    this.sequences.set(question.questionVersionId, sequence);
    const response = responseFor(question.type, value);
    if (response) void this.save(question, response, sequence, 1);
  }

  private async save(question: FlatQuestion, response: Record<string, unknown>, sequence: number, attempt: number): Promise<void> {
    const paper = this.paper();
    if (!paper) return;
    this.saveState.set('Saving…');
    try {
      const saved = await firstValueFrom(
        this.http.post<{ accepted: boolean; deadline: string }>('/api/assessments/v1/take/answers', {
          attemptId: paper.attemptId,
          questionVersionId: question.questionVersionId,
          sectionId: question.sectionId,
          sequence,
          response,
        }),
      );
      this.paper.update((current) => {
        if (!current) return current;
        return {
          ...current,
          sections: current.sections.map((section) =>
            section.sectionId === question.sectionId ? { ...section, deadline: saved.deadline } : section,
          ),
        };
      });
      this.saveState.set(saved.accepted ? 'Saved' : 'Kept the newer answer');
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (shouldRetrySave(status) && attempt < 6) {
        await firstValueFrom(timer(backoffDelay(attempt)));
        await this.save(question, response, sequence, attempt + 1);
        return;
      }
      this.saveState.set(problemMessage(error));
    }
  }

  private tick(): void {
    this.now.set(Date.now());
    if (this.step() !== 'player' || this.finishing) return;
    const paper = this.paper();
    const question = this.current();
    if (!paper || !question) return;
    if (new Date(paper.deadline).getTime() <= Date.now()) {
      void this.submit(true);
      return;
    }
    if (!this.sectionClosed(question)) return;
    const next = this.flat().findIndex((item, index) => index > this.cursor() && !this.sectionClosed(item));
    if (next >= 0) this.cursor.set(next);
    else void this.submit(true);
  }

  private formatRemaining(deadline: string | undefined): string {
    if (!deadline) return '';
    const seconds = Math.max(0, Math.round((new Date(deadline).getTime() - this.now()) / 1000));
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  }

  private storageKey(): string {
    return `shortlist.take.${this.token}`;
  }
}
