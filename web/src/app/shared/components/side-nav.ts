import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLogOut } from '@ng-icons/lucide';
import { SessionStore } from '../../core/session';
import { en } from '../../../locale/en';

interface NavItem {
  path: string;
  label: string;
  permission: string;
  module: string;
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

@Component({
  selector: 'app-side-nav',
  imports: [RouterLink, RouterLinkActive, NgIcon],
  providers: [provideIcons({ lucideLogOut })],
  template: `
    <div class="flex h-full flex-col gap-5 px-3.5 py-6">
      <div class="flex flex-col gap-0.5 px-2.5">
        <p class="font-display text-xl font-semibold text-white" i18n>Shortlist</p>
        <p class="text-xs text-nav-muted" i18n>[COMPANY NAME] · Hiring</p>
      </div>
      @for (group of visibleGroups(); track group.label) {
        <div class="flex flex-col gap-0.5">
          <p class="px-3 pb-1.5 text-xs font-semibold tracking-nav text-nav-muted uppercase">
            {{ group.label }}
          </p>
          @for (item of group.items; track item.path) {
            <a
              #link="routerLinkActive"
              routerLinkActive
              class="flex min-h-11 items-center rounded-control px-3"
              [class.bg-nav-active]="link.isActive"
              [class.font-medium]="link.isActive"
              [class.text-white]="link.isActive"
              [class.text-nav-ink]="!link.isActive"
              [routerLink]="item.path"
            >
              {{ item.label }}
            </a>
          }
        </div>
      }
      <div class="mt-auto flex flex-col gap-1 border-t border-nav-active px-2.5 pt-3.5">
        <p class="font-medium text-white">{{ session.session()?.displayName }}</p>
        <p class="text-xs text-nav-muted">{{ session.session()?.email }}</p>
        <button
          class="flex min-h-11 items-center gap-2 rounded-control px-1 text-nav-ink"
          type="button"
          (click)="signOut()"
        >
          <ng-icon name="lucideLogOut" size="16" />
          {{ copy.signOut }}
        </button>
      </div>
    </div>
  `,
})
export class SideNav {
  protected readonly session = inject(SessionStore);
  protected readonly copy = en;
  private readonly groups: NavGroup[] = [
    {
      label: 'Hiring',
      items: [
        { path: '/openings', label: en.openings, permission: 'Core.Openings.View', module: 'Core' },
        {
          path: '/candidates',
          label: en.candidates,
          permission: 'Core.Candidates.View',
          module: 'Core',
        },
        {
          path: '/workspace',
          label: en.workspace,
          permission: 'Core.HiringUnits.View',
          module: 'Core',
        },
      ],
    },
    {
      label: 'Assessments',
      items: [
        {
          path: '/questions',
          label: en.questions,
          permission: 'Assessments.Questions.Read',
          module: 'Assessments',
        },
        {
          path: '/assessments',
          label: en.assessments,
          permission: 'Assessments.Assessments.Write',
          module: 'Assessments',
        },
        {
          path: '/grading',
          label: en.grading,
          permission: 'Assessments.Grading.Grade',
          module: 'Assessments',
        },
        {
          path: '/results',
          label: en.results,
          permission: 'Assessments.Results.View',
          module: 'Assessments',
        },
      ],
    },
    {
      label: 'Admin',
      items: [
        {
          path: '/people',
          label: en.people,
          permission: 'Identity.Users.Manage',
          module: 'Identity',
        },
      ],
    },
  ];

  protected visibleGroups(): NavGroup[] {
    return this.groups
      .map((group) => ({
        ...group,
        items: group.items.filter(
          (item) => this.session.can(item.permission) && this.session.moduleEnabled(item.module),
        ),
      }))
      .filter((group) => group.items.length > 0);
  }

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    location.assign('/login');
  }
}
