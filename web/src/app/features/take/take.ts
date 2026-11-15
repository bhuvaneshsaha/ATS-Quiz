import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { BrnSheetImports } from '@spartan-ng/brain/sheet';
import { firstValueFrom, timer } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { Api, problemMessage } from '../../core/api';
import { backoffDelay, shouldRetrySave } from '../../shared/answer-queue';

interface Option { id: string; text: string }
interface Presented {
  questionVersionId: string;
  type: string;
  prompt: string;
  options: Option[];
}
interface Section { sectionId: string; name: string; questions: Presented[] }
interface Paper { attemptId: string; deadline: string; sections: Section[] }

@Component({
  selector: 'app-take',
  imports: [BrnSheetImports],
  template: `
    <main class="mx-auto min-h-dvh max-w-2xl px-4 py-8">
      @switch (step()) {
        @case ('welcome') {
          <h1 class="font-serif text-3xl" i18n>Your assessment</h1>
          <p class="mt-3 text-sm" i18n>We will email a one-time code before the timer starts.</p>
          <button class="mt-6 h-11 rounded-lg bg-teal px-4 text-card" type="button" (click)="requestCode()" i18n>Email me a code</button>
        }
        @case ('code') {
          <h1 class="font-serif text-3xl" i18n>Enter your code</h1>
          <form class="mt-6 space-y-3" (submit)="verify($event)">
            <input class="h-11 w-full rounded-lg border border-line px-3" name="code" inputmode="numeric" autocomplete="one-time-code" required />
            <button class="h-11 rounded-lg bg-teal px-4 text-card" i18n>Continue</button>
          </form>
        }
        @case ('check') {
          <h1 class="font-serif text-3xl" i18n>System check</h1>
          @if (narrow()) {
            <p class="mt-3 text-sm" i18n>This test is comfortable on a phone. Ordering questions keep arrow buttons as well as drag and drop.</p>
          } @else {
            <p class="mt-3 text-sm" i18n>Your screen is wide enough. The timer is kept by the server.</p>
          }
          <button class="mt-6 h-11 rounded-lg bg-teal px-4 text-card" type="button" (click)="start()" i18n>Start</button>
        }
        @case ('player') {
          @if (current(); as question) {
            <p class="text-sm text-ink/70">{{ remaining() }}</p>
            <h1 class="mt-2 font-serif text-2xl">{{ question.prompt }}</h1>
            <div class="mt-4 grid gap-2">
              @for (option of question.options; track option.id) {
                <button class="h-11 rounded-lg border border-line bg-card px-3 text-left" type="button" (click)="choose(question, option.id)">{{ option.text }}</button>
              }
            </div>
            <p class="mt-3 text-xs text-ink/60">{{ saveState() }}</p>
          }
          <div class="mt-8 hidden md:flex md:gap-2">
            @for (question of flat(); track question.questionVersionId; let position = $index) {
              <button class="h-11 w-11 rounded-lg border border-line" type="button" (click)="cursor.set(position)">{{ position + 1 }}</button>
            }
          </div>
          <div class="mt-6 md:hidden" brnSheet side="bottom">
            <button brnSheetTrigger class="h-11 w-full rounded-lg border border-line" type="button" i18n>Questions</button>
            <div brnSheetOverlay></div>
            <ng-template brnSheetContent>
              <div class="rounded-t-2xl bg-card p-4">
                @for (question of flat(); track question.questionVersionId; let position = $index) {
                  <button class="h-11 w-full text-left" type="button" (click)="cursor.set(position)">{{ position + 1 }}. {{ question.prompt }}</button>
                }
              </div>
            </ng-template>
          </div>
          <button class="mt-6 h-11 rounded-lg bg-ink px-4 text-card" type="button" (click)="submit()" i18n>Submit</button>
        }
        @case ('done') {
          <h1 class="font-serif text-3xl" i18n>Submitted</h1>
          <p class="mt-3 text-sm" i18n>Your answers are with the hiring team. You can close this page.</p>
        }
      }
      @if (error()) { <p class="mt-4 text-sm text-clay" role="alert">{{ error() }}</p> }
    </main>
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
  private readonly sequences = new Map<string, number>();
  private readonly token = this.route.snapshot.paramMap.get('token') ?? '';

  protected readonly flat = computed(() => this.paper()?.sections.flatMap((section) => section.questions.map((question) => ({ ...question, sectionId: section.sectionId }))) ?? []);
  protected readonly current = computed(() => this.flat()[this.cursor()] ?? null);
  protected readonly remaining = computed(() => {
    const deadline = this.paper()?.deadline;
    if (!deadline) {
      return '';
    }
    const seconds = Math.max(0, Math.round((new Date(deadline).getTime() - Date.now()) / 1000));
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

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
    const sequence = (this.sequences.get(question.questionVersionId) ?? 0) + 1;
    this.sequences.set(question.questionVersionId, sequence);
    void this.save(question, optionId, sequence, 1);
  }

  private async save(question: Presented & { sectionId: string }, optionId: string, sequence: number, attempt: number): Promise<void> {
    const paper = this.paper();
    if (!paper) {
      return;
    }
    this.saveState.set('Saving…');
    try {
      const response = await firstValueFrom(this.http.post<{ accepted: boolean; deadline: string }>('/api/assessments/v1/take/answers', {
        attemptId: paper.attemptId,
        questionVersionId: question.questionVersionId,
        sectionId: question.sectionId,
        sequence,
        response: { optionId },
      }));
      this.paper.update((current) => current ? { ...current, deadline: response.deadline } : current);
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
      await this.api.send('POST', '/api/assessments/v1/take/submit', { attemptId: paper.attemptId });
      this.step.set('done');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
