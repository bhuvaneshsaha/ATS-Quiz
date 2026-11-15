import { Routes } from '@angular/router';
import { staffGuard } from './core/staff.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login').then((module) => module.LoginPage),
  },
  {
    path: 'take/:token',
    loadComponent: () => import('./features/take/take').then((module) => module.TakePage),
  },
  {
    path: '',
    canActivate: [staffGuard],
    loadComponent: () => import('./features/shell/shell').then((module) => module.ShellPage),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'openings' },
      {
        path: 'openings',
        loadComponent: () =>
          import('./features/hiring/openings').then((module) => module.OpeningsPage),
      },
      {
        path: 'openings/:id',
        loadComponent: () =>
          import('./features/hiring/opening-detail').then((module) => module.OpeningDetailPage),
      },
      {
        path: 'candidates',
        loadComponent: () =>
          import('./features/hiring/candidates').then((module) => module.CandidatesPage),
      },
      {
        path: 'workspace',
        loadComponent: () =>
          import('./features/hiring/workspace').then((module) => module.WorkspacePage),
      },
      {
        path: 'questions',
        loadComponent: () =>
          import('./features/assessments/questions').then((module) => module.QuestionsPage),
      },
      {
        path: 'assessments',
        loadComponent: () =>
          import('./features/assessments/assessments').then((module) => module.AssessmentsPage),
      },
      {
        path: 'grading',
        loadComponent: () =>
          import('./features/assessments/grading').then((module) => module.GradingPage),
      },
      {
        path: 'people',
        loadComponent: () => import('./features/admin/people').then((module) => module.PeoplePage),
      },
    ],
  },
];
