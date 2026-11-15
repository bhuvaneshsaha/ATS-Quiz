import { NgTemplateOutlet } from '@angular/common';
import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StatusPill } from './status-pill';

export interface DataColumn<T> {
  header: string;
  value: (row: T) => string;
  detail?: (row: T) => string | null;
  href?: (row: T) => string | null;
  status?: boolean;
}

@Component({
  selector: 'app-data-table',
  imports: [NgTemplateOutlet, RouterLink, StatusPill],
  template: `
    <section class="overflow-hidden rounded-card border border-line bg-surface">
      @if (rows().length > 0) {
        <div class="hidden overflow-x-auto md:block">
          <table class="w-full text-left">
            <thead>
              <tr class="bg-canvas text-xs font-semibold tracking-label text-muted uppercase">
                @for (column of columns(); track column.header) {
                  <th class="px-5 py-2.5 font-semibold">{{ column.header }}</th>
                }
              </tr>
            </thead>
            <tbody class="tabular-nums">
              @for (row of rows(); track track()(row)) {
                <tr class="border-t border-line-soft">
                  @for (column of columns(); track column.header) {
                    <td class="px-5 py-3 align-middle">
                      <ng-container *ngTemplateOutlet="cell; context: { $implicit: row, column }" />
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="md:hidden">
          @for (row of rows(); track track()(row)) {
            <article class="flex flex-col gap-3 border-t border-line-soft p-4 first:border-t-0">
              @for (column of columns(); track column.header) {
                <div class="flex flex-col gap-0.5">
                  <p class="text-xs font-semibold tracking-label text-muted uppercase">
                    {{ column.header }}
                  </p>
                  <ng-container *ngTemplateOutlet="cell; context: { $implicit: row, column }" />
                </div>
              }
            </article>
          }
        </div>
      }
      @if (rows().length === 0) {
        <p class="px-5 py-6 text-muted">{{ empty() }}</p>
      }
    </section>

    <ng-template #cell let-row let-column="column">
      @if (column.status) {
        <app-status-pill [status]="column.value(row)" />
      } @else if (column.href?.(row); as href) {
        <a class="font-semibold text-ink" [routerLink]="href">{{ column.value(row) }}</a>
        @if (column.detail?.(row); as detail) {
          <span class="block text-sm text-muted">{{ detail }}</span>
        }
      } @else {
        <span>{{ column.value(row) }}</span>
        @if (column.detail?.(row); as detail) {
          <span class="block text-sm text-muted">{{ detail }}</span>
        }
      }
    </ng-template>
  `,
})
export class DataTable<T> {
  readonly columns = input.required<DataColumn<T>[]>();
  readonly rows = input.required<readonly T[]>();
  readonly track = input.required<(row: T) => string>();
  readonly empty = input('Nothing to show.');
}
