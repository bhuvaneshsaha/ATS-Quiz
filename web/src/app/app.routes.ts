import { Routes } from '@angular/router';
import { permissionGuard } from './core/permission.guard';
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
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./features/shell/home').then((module) => module.HomePage),
      },
      {
        path: 'openings',
        canActivate: [permissionGuard('Core.Openings.View')],
        loadComponent: () =>
          import('./features/hiring/openings').then((module) => module.OpeningsPage),
      },
      {
        path: 'openings/:id',
        canActivate: [permissionGuard('Core.Openings.View')],
        loadComponent: () =>
          import('./features/hiring/opening-detail').then((module) => module.OpeningDetailPage),
      },
      {
        path: 'candidates',
        canActivate: [permissionGuard('Core.Candidates.View')],
        loadComponent: () =>
          import('./features/hiring/candidates').then((module) => module.CandidatesPage),
      },
      {
        path: 'workspace',
        canActivate: [permissionGuard('Core.HiringUnits.View')],
        loadComponent: () =>
          import('./features/hiring/workspace').then((module) => module.WorkspacePage),
      },
      {
        path: 'questions',
        canActivate: [permissionGuard('Assessments.Questions.Read')],
        loadComponent: () =>
          import('./features/assessments/questions').then((module) => module.QuestionsPage),
      },
      {
        path: 'questions/:id',
        canActivate: [permissionGuard('Assessments.Questions.Read')],
        loadComponent: () =>
          import('./features/assessments/question-detail').then((module) => module.QuestionDetailPage),
      },
      {
        path: 'assessments',
        canActivate: [permissionGuard('Assessments.Assessments.Write')],
        loadComponent: () =>
          import('./features/assessments/assessments').then((module) => module.AssessmentsPage),
      },
      {
        path: 'grading',
        canActivate: [permissionGuard('Assessments.Grading.Grade')],
        loadComponent: () =>
          import('./features/assessments/grading').then((module) => module.GradingPage),
      },
      {
        path: 'results',
        canActivate: [permissionGuard('Assessments.Results.View')],
        loadComponent: () =>
          import('./features/assessments/results').then((module) => module.ResultsPage),
      },
      {
        path: 'people',
        canActivate: [permissionGuard('Identity.Users.Manage')],
        loadComponent: () => import('./features/admin/people').then((module) => module.PeoplePage),
      },
    ],
  },
];
