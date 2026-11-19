import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../../core/api';
import { Card } from '../../shared/components/card';
import { DataColumn, DataTable } from '../../shared/components/data-table';
import { EmptyState } from '../../shared/components/empty-state';
import { PageHeader } from '../../shared/components/page-header';

interface ResultRow {
  attemptId: string;
  applicationId: string;
  candidateName: string;
  openingId?: string | null;
  openingTitle?: string;
  assessmentName?: string;
  total: number;
  max: number;
  passed: boolean;
  status: string;
  sectionScoresJson?: string;
  reportKind?: string;
}

@Component({
  selector: 'app-results',
  imports: [Card, DataTable, EmptyState, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Results</ng-container>
        <ng-container pageSubtitle i18n>Scores from submitted assessments</ng-container>
      </app-page-header>
      @if (rows().length === 0 && !error()) {
        <app-empty-state message="No results yet." />
      } @else {
        <app-data-table [columns]="columns" [rows]="rows()" [track]="track" empty="No results yet." />
      }
      @if (rows().length > 0) {
        <app-card>
          <h2 i18n>Reports</h2>
          <ul class="flex flex-col gap-2">
            @for (row of rows(); track row.attemptId) {
              <li>
                <a [href]="'/api/assessments/v1/results/' + row.attemptId + '/report'">{{ row.candidateName }} {{ reportLabel(row) }}</a>
              </li>
            }
          </ul>
        </app-card>
      }
      @if (error()) {
        <p class="text-sm text-danger" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class ResultsPage {
  private readonly api = inject(Api);
  protected readonly rows = signal<ResultRow[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly columns: DataColumn<ResultRow>[] = [
    { header: 'Candidate', value: (row) => row.candidateName },
    { header: 'Opening', value: (row) => row.openingTitle || '—', href: (row) => (row.openingId ? `/openings/${row.openingId}` : null) },
    { header: 'Assessment', value: (row) => row.assessmentName || '—' },
    { header: 'Score', value: (row) => `${row.total} / ${row.max}` },
    { header: 'Sections', value: (row) => row.sectionScoresJson || '—' },
    { header: 'Result', value: (row) => (row.passed ? 'Passed' : 'Not passed'), status: true },
    { header: 'Status', value: (row) => row.status, status: true },
  ];
  protected readonly track = (row: ResultRow) => row.attemptId;

  protected reportLabel(row: ResultRow): string {
    return row.reportKind === 'pdf' ? 'PDF' : 'Score summary';
  }

  constructor() {
    void this.api
      .get<ResultRow[]>('/api/assessments/v1/results')
      .then((rows) => this.rows.set(rows))
      .catch((error) => this.error.set(problemMessage(error)));
  }
}
