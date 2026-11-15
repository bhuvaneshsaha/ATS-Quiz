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
  passed: boolean | null;
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
                  <p class="font-semibold tabular-nums">
                    {{ application.score ?? '—' }}
                  </p>
                  <app-status-pill [status]="application.stageName" />
                  <app-form-field label="Move to">
                    <select appControl (change)="move(application.id, $any($event.target).value)">
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
              <form class="flex flex-col gap-3" (submit)="bind($event)">
                <app-form-field label="Published assessment" [required]="true">
                  <select appControl name="version">
                    @for (assessment of assessments(); track assessment.id) {
                      @if (assessment.current?.isPublished) {
                        <option [value]="assessment.current?.id">{{ assessment.name }}</option>
                      }
                    }
                  </select>
                </app-form-field>
                <button appButton variant="secondary" i18n>Change test</button>
              </form>
              <p class="text-sm text-muted" i18n>
                The published version stays pinned, so scores on this opening stay comparable.
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
      .catch(() => undefined);
  }

  protected count(stageId: string): number {
    return this.counts().get(stageId) ?? 0;
  }

  private async reload(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    this.opening.set(await this.api.get<OpeningDetails>(`/api/core/v1/openings/${id}`));
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

  protected async move(applicationId: string, stageId: string): Promise<void> {
    if (!stageId) {
      return;
    }

    this.error.set(null);
    try {
      await this.api.send(
        'POST',
        `/api/core/v1/openings/${this.opening()?.id}/applications/${applicationId}/stage`,
        { toStageId: stageId },
      );
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
      await this.api.send(
        'POST',
        '/api/assessments/v1/bindings',
        {
          openingStageId: stage.id,
          assessmentVersionId: version,
          passMarkOverride: null,
          validDays: 7,
          extraTimePercent: 0,
        },
        true,
      );
      this.notice.set('The test is attached to the assessment stage.');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
