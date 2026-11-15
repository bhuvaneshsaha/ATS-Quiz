import { Component } from '@angular/core';

@Component({
  selector: 'app-page-header',
  template: `
    <header class="flex flex-wrap items-end justify-between gap-4">
      <div class="flex min-w-0 flex-col gap-1">
        <div class="flex flex-wrap items-center gap-3">
          <h1><ng-content select="[pageTitle]" /></h1>
          <ng-content select="[pageMeta]" />
        </div>
        <div class="text-muted"><ng-content select="[pageSubtitle]" /></div>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <ng-content />
      </div>
    </header>
  `,
})
export class PageHeader {}
