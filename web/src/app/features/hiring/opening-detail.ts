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
      <a routerLink="/openings" i18n>Back to openings</a>
      @if (opening(); as item) {
        <app-page-header>
          <ng-container pageTitle>{{ item.title }}</ng-container>
          <ng-container pageSubtitle>{{ item.code }}</ng-container>
          <app-status-pill [status]="item.status" />
          @if (item.status === 'Draft') {
            <button appButton type="button" (click)="open()" i18n>Open this role</button>
          }
        </app-page-header>

        <div class="flex flex-col gap-5 lg:flex-row lg:items-start">
          <div class="flex min-w-0 flex-1 flex-col gap-5">
            <app-card>
              <h2 i18n>Pipeline</h2>
              @for (stage of item.stages; track stage.id) {
                <div class="flex flex-col gap-2 md:flex-row md:items-center">
                  <p>{{ stage.name }}</p>
                  <app-progress-bar
                    class="md:flex-1"
                    [value]="count(stage.id)"
                    [max]="item.applications.length || 1"
                  />
                  <p class="text-end font-semibold tabular-nums">{{ count(stage.id) }}</p>
                </div>
              }
            </app-card>

            <section class="overflow-hidden rounded-card border border-line bg-surface">
              <header class="border-b border-line px-5 py-4">
                <h2 i18n>Candidates</h2>
              </header>
              @for (application of item.applications; track application.id) {
                <article class="flex flex-col gap-3 border-t border-line-soft px-5 py-4">
                  <div class="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p class="font-semibold">{{ application.candidateName }}</p>
                      <p class="text-sm text-muted">{{ application.candidateEmail }}</p>
                    </div>
                    <app-status-pill [status]="application.stageName" />
                  </div>
                  @if (application.score !== null) {
                    <p class="tabular-nums">
                      {{ application.score }} · {{ application.passed ? 'Passed' : 'Not passed' }}
                    </p>
                  }
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

          <app-card class="w-full lg:w-80">
            <h2 i18n>Attach a published test</h2>
            <form class="flex flex-col gap-3" (submit)="bind($event)">
              <app-form-field label="Assessment" [required]="true">
                <select appControl name="version">
                  @for (assessment of assessments(); track assessment.id) {
                    @if (assessment.current?.isPublished) {
                      <option [value]="assessment.current?.id">{{ assessment.name }}</option>
                    }
                  }
                </select>
              </app-form-field>
              <button appButton i18n>Bind to the assessment stage</button>
            </form>
          </app-card>
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

  protected async open(): Promise<void> {
    await this.api.send('POST', `/api/core/v1/openings/${this.opening()?.id}/status`, {
      status: 'Open',
    });
    await this.reload();
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
