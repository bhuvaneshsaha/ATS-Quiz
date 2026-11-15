import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
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
      <app-side-nav class="hidden lg:block" />
      <div class="min-w-0">
        <header
          class="flex items-center justify-between border-b border-line bg-surface px-4 py-3 lg:hidden"
        >
          <p class="font-display text-lg font-semibold" i18n>Shortlist</p>
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
              <app-side-nav />
            </ng-template>
          </div>
        </header>
        <router-outlet />
      </div>
    </div>
  `,
})
export class ShellPage {}
