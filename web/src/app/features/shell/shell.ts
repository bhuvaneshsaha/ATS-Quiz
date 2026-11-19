import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SessionStore } from '../../core/session';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMenu } from '@ng-icons/lucide';
import { BrnButton } from '@spartan-ng/brain/button';
import { BrnSheetImports } from '@spartan-ng/brain/sheet';
import { SideNav } from '../../shared/components/side-nav';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, NgIcon, BrnButton, BrnSheetImports, SideNav],
  providers: [provideIcons({ lucideMenu })],
  template: `
    <div class="staff-frame">
      <a
        class="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:bg-surface focus:px-3 focus:py-2"
        href="#content"
        >Skip to content</a
      >
      <app-side-nav class="hidden lg:block" />
      <div class="min-w-0">
        <header
          class="flex items-center justify-between border-b border-line bg-surface px-4 py-3 lg:hidden"
        >
          <p class="font-display text-lg font-semibold">{{ session.session()?.workspaceName || 'Shortlist' }}</p>
          <div brnSheet side="left">
            <button
              brnButton
              brnSheetTrigger
              type="button"
              class="grid size-11 place-items-center rounded-control border border-line-strong bg-surface"
              i18n-aria-label
              aria-label="Open menu"
            >
              <ng-icon name="lucideMenu" size="20" />
            </button>
            <div brnSheetOverlay class="bg-ink/40"></div>
            <ng-template brnSheetContent>
              <div class="flex h-dvh w-screen flex-col bg-nav">
                <button brnSheetClose autofocus type="button" class="m-3 min-h-11 self-end text-nav-ink" aria-label="Close menu">
                  Close
                </button>
                <app-side-nav />
              </div>
            </ng-template>
          </div>
        </header>
        <main id="content">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
export class ShellPage {
  protected readonly session = inject(SessionStore);
}
