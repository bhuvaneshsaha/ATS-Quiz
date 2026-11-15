import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';

interface Stage {
  id: string;
  name: string;
  type: string;
}

interface ApplicationRow {
  id: string;
  candidateName: string;
  candidateEmail: string;
  currentStageId: string;
  stageName: string;
  status: string;
  score: number | null;
  passed: boolean | null;
}

interface OpeningDetails {
  id: string;
  code: string;
  title: string;
  status: string;
  stages: Stage[];
  applications: ApplicationRow[];
}

interface AssessmentRow {
  id: string;
  name: string;
  current: { id: string; isPublished: boolean } | null;
}

@Component({
  selector: 'app-opening-detail',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-5xl px-4 py-6">
      <a class="text-sm underline" routerLink="/openings" i18n>Back to openings</a>
      @if (opening(); as item) {
        <h1 class="mt-3 font-serif text-3xl">{{ item.title }}</h1>
        <p class="text-sm text-ink/70">{{ item.code }} · {{ item.status }}</p>
        @if (item.status === 'Draft') {
          <button class="mt-4 h-11 rounded-lg bg-teal px-4 text-card" type="button" (click)="open()" i18n>Open this role</button>
        }
        <h2 class="mt-8 font-serif text-xl" i18n>Candidates</h2>
        <div class="mt-3 grid gap-3">
          @for (application of item.applications; track application.id) {
            <article class="rounded-2xl border border-line bg-card p-4">
              <p class="font-medium">{{ application.candidateName }}</p>
              <p class="text-sm text-ink/70">{{ application.candidateEmail }} · {{ application.stageName }}</p>
              @if (application.score !== null) {
                <p class="text-sm">{{ application.score }} · {{ application.passed ? 'Passed' : 'Not passed' }}</p>
              }
              <label class="mt-3 block text-sm" i18n>
                Move to
                <select class="mt-1 h-11 w-full rounded-lg border border-line px-3 md:w-72" (change)="move(application.id, $any($event.target).value)">
                  <option value="">Select a stage</option>
                  @for (stage of item.stages; track stage.id) {
                    <option [value]="stage.id">{{ stage.name }}</option>
                  }
                </select>
              </label>
            </article>
          } @empty {
            <p class="text-sm text-ink/70" i18n>No one has been added yet. Add a candidate first.</p>
          }
        </div>
        <form class="mt-8 grid gap-3 rounded-2xl border border-line bg-card p-4 md:grid-cols-2" (submit)="bind($event)">
          <h2 class="font-serif text-xl md:col-span-2" i18n>Attach a published test</h2>
          <label class="text-sm" i18n>Assessment
            <select class="mt-1 h-11 w-full rounded-lg border border-line px-3" name="version">
              @for (assessment of assessments(); track assessment.id) {
                @if (assessment.current?.isPublished) {
                  <option [value]="assessment.current?.id">{{ assessment.name }}</option>
                }
              }
            </select>
          </label>
          <button class="h-11 self-end rounded-lg bg-ink px-4 text-card" i18n>Bind to the assessment stage</button>
        </form>
        @if (notice()) { <p class="mt-4 text-sm">{{ notice() }}</p> }
        @if (error()) { <p class="mt-4 text-sm text-clay" role="alert">{{ error() }}</p> }
      }
    </section>
  `,
})
export class OpeningDetailPage {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  protected readonly opening = signal<OpeningDetails | null>(null);
  protected readonly assessments = signal<AssessmentRow[]>([]);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly copy = en;

  constructor() {
    void this.reload();
    void this.api.get<AssessmentRow[]>('/api/assessments/v1/assessments').then((rows) => this.assessments.set(rows)).catch(() => undefined);
  }

  private async reload(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    this.opening.set(await this.api.get<OpeningDetails>(`/api/core/v1/openings/${id}`));
  }

  protected async open(): Promise<void> {
    await this.api.send('POST', `/api/core/v1/openings/${this.opening()?.id}/status`, { status: 'Open' });
    await this.reload();
  }

  protected async move(applicationId: string, stageId: string): Promise<void> {
    if (!stageId) {
      return;
    }

    this.error.set(null);
    try {
      await this.api.send('POST', `/api/core/v1/openings/${this.opening()?.id}/applications/${applicationId}/stage`, { toStageId: stageId });
      this.notice.set(en.inviteQueued);
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async bind(event: Event): Promise<void> {
    event.preventDefault();
    const form = event.target as HTMLFormElement;
    const version = new FormData(form).get('version')?.toString();
    const stage = this.opening()?.stages.find((item) => item.type === 'Assessment');
    if (!version || !stage) {
      this.error.set('Publish an assessment and keep the assessment stage.');
      return;
    }

    try {
      await this.api.send('POST', '/api/assessments/v1/bindings', {
        openingStageId: stage.id,
        assessmentVersionId: version,
        passMarkOverride: null,
        validDays: 7,
        extraTimePercent: 0,
      }, true);
      this.notice.set('The test is attached to the assessment stage.');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
