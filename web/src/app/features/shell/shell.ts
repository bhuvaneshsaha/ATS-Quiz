import { NgTemplateOutlet } from '@angular/common';
import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLogOut, lucideMenu } from '@ng-icons/lucide';
import { BrnButton } from '@spartan-ng/brain/button';
import { BrnSheetImports } from '@spartan-ng/brain/sheet';
import { SessionStore } from '../../core/session';
import { en } from '../../../locale/en';

interface NavItem {
  path: string;
  label: string;
  permission: string;
  module: string;
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgTemplateOutlet, NgIcon, BrnButton, BrnSheetImports],
  providers: [provideIcons({ lucideMenu, lucideLogOut })],
  template: `
    <div class="min-h-dvh lg:grid lg:grid-cols-[16rem_1fr]">
      <aside class="hidden border-r border-line bg-card lg:flex lg:flex-col">
        <ng-container *ngTemplateOutlet="nav" />
      </aside>
      <div>
        <header class="flex items-center justify-between border-b border-line px-4 py-3 lg:hidden">
          <p class="font-serif text-xl" i18n>Shortlist</p>
          <div brnSheet side="left">
            <button brnButton brnSheetTrigger type="button" class="grid h-11 w-11 place-items-center rounded-lg border border-line" i18n-aria-label aria-label="Open menu">
              <ng-icon name="lucideMenu" />
            </button>
            <div brnSheetOverlay></div>
            <ng-template brnSheetContent>
              <div class="h-dvh w-72 bg-card p-4">
                <ng-container *ngTemplateOutlet="nav" />
              </div>
            </ng-template>
          </div>
        </header>
        <router-outlet />
      </div>
    </div>

    <ng-template #nav>
      <div class="flex h-full flex-col p-4">
        <p class="font-serif text-2xl" i18n>Shortlist</p>
        <p class="mt-1 text-sm text-ink/70">{{ session.session()?.displayName }}</p>
        <nav class="mt-6 flex flex-col gap-1">
          @for (item of visible(); track item.path) {
            <a class="rounded-lg px-3 py-2 text-sm hover:bg-paper" routerLinkActive="bg-paper font-medium" [routerLink]="item.path">{{ item.label }}</a>
          }
        </nav>
        <button brnButton class="mt-auto flex h-11 items-center gap-2 rounded-lg px-3 text-sm" type="button" (click)="signOut()">
          <ng-icon name="lucideLogOut" /> {{ copy.signOut }}
        </button>
      </div>
    </ng-template>
  `,
})
export class ShellPage {
  protected readonly session = inject(SessionStore);
  protected readonly copy = en;
  private readonly items: NavItem[] = [
    { path: '/openings', label: en.openings, permission: 'Core.Openings.View', module: 'Core' },
    { path: '/candidates', label: en.candidates, permission: 'Core.Candidates.View', module: 'Core' },
    { path: '/workspace', label: en.workspace, permission: 'Core.HiringUnits.View', module: 'Core' },
    { path: '/questions', label: en.questions, permission: 'Assessments.Questions.Write', module: 'Assessments' },
    { path: '/assessments', label: en.assessments, permission: 'Assessments.Assessments.Write', module: 'Assessments' },
    { path: '/grading', label: en.grading, permission: 'Assessments.Grading.Grade', module: 'Assessments' },
    { path: '/people', label: en.people, permission: 'Identity.Users.Manage', module: 'Identity' },
  ];

  protected visible(): NavItem[] {
    return this.items.filter((item) => this.session.can(item.permission) && this.session.moduleEnabled(item.module));
  }

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    location.assign('/login');
  }
}
