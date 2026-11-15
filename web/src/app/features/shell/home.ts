import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { SessionStore } from '../../core/session';
import { EmptyState } from '../../shared/components/empty-state';
import { PageHeader } from '../../shared/components/page-header';

const destinations = [
  { path: '/openings', permission: 'Core.Openings.View' },
  { path: '/candidates', permission: 'Core.Candidates.View' },
  { path: '/workspace', permission: 'Core.HiringUnits.View' },
  { path: '/questions', permission: 'Assessments.Questions.Read' },
  { path: '/assessments', permission: 'Assessments.Assessments.Write' },
  { path: '/grading', permission: 'Assessments.Grading.Grade' },
  { path: '/results', permission: 'Assessments.Results.View' },
  { path: '/people', permission: 'Identity.Users.Manage' },
];

@Component({
  selector: 'app-home',
  imports: [EmptyState, PageHeader],
  template: `
    <section class="page">
      <app-page-header>
        <ng-container pageTitle i18n>Welcome</ng-container>
      </app-page-header>
      <app-empty-state message="Your account does not have a role yet. Ask an administrator to assign one." />
    </section>
  `,
})
export class HomePage {
  constructor() {
    const session = inject(SessionStore);
    const router = inject(Router);
    const next = destinations.find((item) => session.can(item.permission));
    if (next) {
      void router.navigateByUrl(next.path);
    }
  }
}
