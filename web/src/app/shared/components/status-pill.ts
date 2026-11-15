import { Component, input } from '@angular/core';

export type StatusTone = 'success' | 'warning' | 'info' | 'danger' | 'neutral' | 'strong';

const tones: Record<StatusTone, string> = {
  success: 'bg-accent-tint text-accent-strong',
  warning: 'bg-warn-tint text-warn',
  info: 'bg-info-tint text-info',
  danger: 'bg-danger-tint text-danger',
  neutral: 'bg-line-soft text-ink-soft',
  strong: 'bg-ink text-white',
};

const groups: Record<StatusTone, string[]> = {
  success: ['open', 'live', 'published', 'graded', 'passed', 'advanced', 'correct'],
  warning: ['needs grading', 'in review', 'inreview', 'on hold', 'onhold', 'at risk', 'to grade'],
  info: ['in progress', 'inprogress', 'client', 'partial', 'saved section'],
  danger: ['rejected', 'expired', 'archived', 'incorrect', 'closed', 'closedunfilled'],
  neutral: ['draft', 'invited', 'internal', 'manual', 'department'],
  strong: ['hired', 'always on', 'filled'],
};

const labels: Record<string, string> = {
  onhold: 'On hold',
  inreview: 'In review',
  inprogress: 'In progress',
  closedunfilled: 'Closed',
};

export function toneFor(status: string): StatusTone {
  const key = status.trim().toLowerCase();
  for (const tone of Object.keys(groups) as StatusTone[]) {
    if (groups[tone].includes(key)) {
      return tone;
    }
  }
  return 'neutral';
}

export function labelFor(status: string): string {
  return labels[status.trim().toLowerCase()] ?? status;
}

@Component({
  selector: 'app-status-pill',
  template: `
    <span
      class="inline-flex items-center rounded-pill px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap {{
        toneClass()
      }}"
    >
      {{ labelFor(status()) }}
    </span>
  `,
})
export class StatusPill {
  readonly status = input.required<string>();
  protected readonly labelFor = labelFor;
  protected toneClass(): string {
    return tones[toneFor(this.status())];
  }
}
