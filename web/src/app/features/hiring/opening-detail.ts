import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api, problemMessage } from '../../core/api';
import { en } from '../../../locale/en';
import { Button } from '../../shared/ui/button';
import { FieldControl } from '../../shared/ui/field';
import { Card } from '../../shared/components/card';
import { FormField } from '../../shared/components/form-field';
import { PageHeader } from '../../shared/components/page-header';
import { ProgressBar } from '../../shared/components/progress-bar';
import { StatusPill } from '../../shared/components/status-pill';

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
  maxScore: number | null;
  passed: boolean | null;
  resultStatus: string | null;
  attemptId: string | null;
}

interface OpeningDetails {
  id: string;
  code: string;
  title: string;
  status: string;
  positions: number;
  location: string | null;
  seniority: string | null;
  reason: string | null;
  targetDate: string | null;
  externalId: string | null;
  stages: Stage[];
  applications: ApplicationRow[];
}

interface AssessmentRow {
  id: string;
  name: string;
  current: { id: string; isPublished: boolean } | null;
}
interface BindingRow {
  id: string;
  assessmentVersionId: string;
  assessmentName: string;
  validDays: number;
  extraTimePercent: number;
  passMarkOverride: number | null;
}

@Component({
  selector: 'app-opening-detail',
  imports: [RouterLink, Button, FieldControl, Card, FormField, PageHeader, ProgressBar, StatusPill],
  template: `
    <section class="page">
      @if (opening(); as item) {
        <a routerLink="/openings" i18n>Back to openings</a>
        <app-page-header>
          <ng-container pageTitle>{{ item.title }}</ng-container>
          <app-status-pill pageMeta [status]="item.status" />
          <ng-container pageSubtitle>{{ summary(item) }}</ng-container>
          @if (item.status === 'Open' || item.status === 'OnHold') {
            <button
              appButton
              variant="secondary"
              type="button"
              (click)="setStatus('ClosedUnfilled')"
              i18n
            >
              Close opening
            </button>
          }
          @if (item.status === 'Open') {
            <button appButton variant="secondary" type="button" (click)="setStatus('OnHold')" i18n>
              Put on hold
            </button>
          }
          @if (item.status === 'Draft') {
            <button appButton type="button" (click)="setStatus('Open')" i18n>
              Open for hiring
            </button>
          }
          @if (item.status === 'OnHold') {
            <button appButton type="button" (click)="setStatus('Open')" i18n>Reopen</button>
          }
          @if (item.status === 'Open') {
            <a appButton routerLink="/candidates" i18n>Invite candidates</a>
          }
        </app-page-header>

        <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
          <div class="flex min-w-0 flex-1 flex-col gap-5">
            <app-card>
              <div class="flex flex-wrap items-baseline justify-between gap-3">
                <h2 i18n>Pipeline</h2>
                <p class="text-ink-soft">
                  {{ item.applications.length }} candidates · {{ item.positions }} positions
                </p>
              </div>
              @for (stage of item.stages; track stage.id) {
                <div class="flex flex-col gap-2 md:flex-row md:items-center">
                  <p class="font-medium md:w-36">{{ stage.name }}</p>
                  <app-progress-bar
                    class="md:flex-1"
                    size="bar"
                    track="soft"
                    [value]="count(stage.id)"
                    [max]="item.applications.length || 1"
                  />
                  <p class="text-end font-semibold tabular-nums md:w-8">{{ count(stage.id) }}</p>
                </div>
              }
            </app-card>

            <section class="overflow-hidden rounded-card border border-line bg-surface">
              <header class="border-b border-line px-5 py-4">
                <h2 i18n>Candidates</h2>
              </header>
              @for (application of item.applications; track application.id) {
                <article
                  class="grid gap-3 border-t border-line-soft px-5 py-4 md:grid-cols-4 md:items-center"
                >
                  <div class="min-w-0">
                    <p class="font-semibold">{{ application.candidateName }}</p>
                    <p class="truncate text-sm text-muted">{{ application.candidateEmail }}</p>
                  </div>
                  <div>
                    <p class="text-xs font-semibold tracking-label text-muted uppercase" i18n>Score</p>
                    <p class="font-semibold tabular-nums">{{ scoreLabel(application) }}</p>
                    @if (application.attemptId) {
                      <a class="text-sm" [href]="'/api/assessments/v1/results/' + application.attemptId + '/report'" i18n>PDF report</a>
                    }
                  </div>
                  <app-status-pill [status]="application.stageName" />
                  <app-form-field label="Move to">
                    <select appControl (change)="move(application.id, $any($event.target))">
                      <option value="">Select a stage</option>
                      @for (stage of item.stages; track stage.id) {
                        <option [value]="stage.id">{{ stage.name }}</option>
                      }
                    </select>
                  </app-form-field>
                </article>
              } @empty {
                <p class="px-5 py-6 text-muted" i18n>
                  No one has been added yet. Add a candidate first.
                </p>
              }
            </section>
          </div>

          <div class="flex w-full flex-col gap-5 lg:w-80">
            <app-card>
              <h2 i18n>Stages</h2>
              @for (stage of item.stages; track stage.id; let index = $index) {
                <div class="flex items-center gap-3">
                  <span
                    class="grid size-6 shrink-0 place-items-center rounded-pill bg-ink text-xs font-semibold text-white"
                    >{{ index + 1 }}</span
                  >
                  <span class="min-w-0 flex-1 font-medium">{{ stage.name }}</span>
                  <app-status-pill [status]="stage.type" />
                </div>
              }
              <p class="text-sm text-muted" i18n>
                Manual stages are moved by a recruiter. An assessment stage can run a published
                test.
              </p>
            </app-card>

            <app-card>
              <h2 i18n>Details</h2>
              @for (row of details(item); track row.label) {
                <div class="flex justify-between gap-3">
                  <span class="text-muted">{{ row.label }}</span>
                  <span class="text-end font-medium">{{ row.value }}</span>
                </div>
              }
            </app-card>

            <app-card>
              <h2 i18n>Screening test</h2>
              @if (pinned(); as current) {
                <p class="text-sm text-ink-soft">
                  Pinned: {{ current.assessmentName }} · {{ current.validDays }} days · {{ current.extraTimePercent }}% extra time
                </p>
              } @else {
                <p class="text-sm text-muted" i18n>No test is pinned to the assessment stage yet.</p>
              }
              <form class="flex flex-col gap-3" (submit)="bind($event)">
                <app-form-field label="Published assessment" [required]="true">
                  <select appControl name="version" required>
                    <option value="" i18n>Select</option>
                    @for (assessment of assessments(); track assessment.id) {
                      @if (assessment.current?.isPublished) {
                        <option [value]="assessment.current?.id">{{ assessment.name }}</option>
                      }
                    }
                  </select>
                </app-form-field>
                <app-form-field label="Valid for days">
                  <input appControl name="validDays" type="number" min="1" value="7" />
                </app-form-field>
                <app-form-field label="Extra time %">
                  <input appControl name="extraTime" type="number" min="0" value="0" />
                </app-form-field>
                <app-form-field label="Pass mark override %">
                  <input appControl name="passMark" type="number" min="0" max="100" />
                </app-form-field>
                <button appButton variant="secondary">{{ pinned() ? 'Replace test' : 'Attach test' }}</button>
              </form>
              <p class="text-sm text-muted" i18n>
                Replacing the test keeps one version on this stage. People already here, who have not started, are invited.
              </p>
            </app-card>
          </div>
        </div>
        @if (notice()) {
          <p class="text-sm text-ink-soft">{{ notice() }}</p>
        }
        @if (error()) {
          <p class="text-sm text-danger" role="alert">{{ error() }}</p>
        }
      }
    </section>
  `,
})
export class OpeningDetailPage {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  protected readonly opening = signal<OpeningDetails | null>(null);
  protected readonly assessments = signal<AssessmentRow[]>([]);
  protected readonly pinned = signal<BindingRow | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly copy = en;
  protected readonly counts = computed(() => {
    const counts = new Map<string, number>();
    for (const application of this.opening()?.applications ?? []) {
      counts.set(application.currentStageId, (counts.get(application.currentStageId) ?? 0) + 1);
    }
    return counts;
  });

  constructor() {
    void this.reload();
    void this.api
      .get<AssessmentRow[]>('/api/assessments/v1/assessments')
      .then((rows) => this.assessments.set(rows))
      .catch((error) => this.error.set(problemMessage(error)));
  }

  protected scoreLabel(application: ApplicationRow): string {
    if (application.score === null || application.score === undefined) {
      return '—';
    }
    const mark = application.maxScore === null || application.maxScore === undefined ? '' : ` / ${application.maxScore}`;
    const outcome = application.passed === null || application.passed === undefined ? '' : application.passed ? ' · Passed' : ' · Not passed';
    const status = application.resultStatus ? ` · ${application.resultStatus}` : '';
    return `${application.score}${mark}${outcome}${status}`;
  }

  protected count(stageId: string): number {
    return this.counts().get(stageId) ?? 0;
  }

  private async reload(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    const opening = await this.api.get<OpeningDetails>(`/api/core/v1/openings/${id}`);
    this.opening.set(opening);
    const stage = opening.stages.find((item) => item.type === 'Assessment');
    if (!stage) {
      this.pinned.set(null);
      return;
    }
    try {
      const bindings = await this.api.get<BindingRow[]>(`/api/assessments/v1/bindings?openingStageId=${stage.id}`);
      this.pinned.set(bindings[0] ?? null);
    } catch {
      this.pinned.set(null);
    }
  }

  protected summary(item: OpeningDetails): string {
    const parts = [item.code];
    if (item.location) {
      parts.push(item.location);
    }
    if (item.targetDate) {
      parts.push(item.targetDate);
    }
    return parts.join(' · ');
  }

  protected details(item: OpeningDetails): { label: string; value: string }[] {
    return [
      { label: 'Positions', value: String(item.positions) },
      { label: 'Location', value: item.location || 'Not set' },
      { label: 'Seniority', value: item.seniority || 'Not set' },
      { label: 'Reason', value: item.reason || 'Not set' },
      { label: 'Target', value: item.targetDate || 'Not set' },
      { label: 'External ID', value: item.externalId || 'None' },
    ];
  }

  protected async setStatus(status: string): Promise<void> {
    this.error.set(null);
    try {
      await this.api.send('POST', `/api/core/v1/openings/${this.opening()?.id}/status`, { status });
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async move(applicationId: string, select: HTMLSelectElement): Promise<void> {
    const stageId = select.value;
    if (!stageId) {
      return;
    }

    this.error.set(null);
    this.notice.set(null);
    try {
      await this.api.send(
        'POST',
        `/api/core/v1/openings/${this.opening()?.id}/applications/${applicationId}/stage`,
        { toStageId: stageId },
      );
      this.notice.set(en.inviteQueued);
      select.value = '';
      await this.reload();
    } catch (error) {
      this.notice.set(null);
      this.error.set(problemMessage(error));
      select.value = '';
    }
  }

  protected async bind(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set(null);
    const form = event.target as HTMLFormElement;
    const data = new FormData(form);
    const version = data.get('version')?.toString();
    const stage = this.opening()?.stages.find((item) => item.type === 'Assessment');
    const passMark = data.get('passMark')?.toString();
    if (!version || !stage) {
      this.error.set('Publish an assessment and keep the assessment stage.');
      return;
    }

    this.notice.set(null);
    try {
      await this.api.send(
        'POST',
        '/api/assessments/v1/bindings',
        {
          openingStageId: stage.id,
          assessmentVersionId: version,
          passMarkOverride: passMark ? Number(passMark) : null,
          validDays: Number(data.get('validDays') || 7),
          extraTimePercent: Number(data.get('extraTime') || 0),
        },
        true,
      );
      this.notice.set('The test is pinned. Candidates already on this stage, who have not started, are invited.');
      await this.reload();
    } catch (error) {
      this.notice.set(null);
      this.error.set(problemMessage(error));
    }
  }
}
