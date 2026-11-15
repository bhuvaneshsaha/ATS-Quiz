import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

function luminance(hex: string): number {
  const channels = hex
    .replace('#', '')
    .match(/../g)!
    .map((pair) => {
      const value = parseInt(pair, 16) / 255;
      return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
    });
  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}

function contrast(foreground: string, background: string): number {
  const lighter = Math.max(luminance(foreground), luminance(background));
  const darker = Math.min(luminance(foreground), luminance(background));
  return (lighter + 0.05) / (darker + 0.05);
}

describe('design tokens', () => {
  const css = readFileSync('src/styles/tokens.css', 'utf8');

  function token(name: string): string {
    const match = css.match(new RegExp(`${name}:\\s*(#[0-9a-fA-F]{6})`));
    if (!match) {
      throw new Error(`Missing ${name}`);
    }
    return match[1];
  }

  it('keeps body, label, and status text at 4.5:1', () => {
    const pairs: [string, string][] = [
      ['--color-ink', '--color-canvas'],
      ['--color-ink', '--color-surface'],
      ['--color-ink-soft', '--color-surface'],
      ['--color-muted', '--color-canvas'],
      ['--color-muted', '--color-surface'],
      ['--color-accent-strong', '--color-accent-tint'],
      ['--color-warn', '--color-warn-tint'],
      ['--color-info', '--color-info-tint'],
      ['--color-danger', '--color-danger-tint'],
      ['--color-ink-soft', '--color-line-soft'],
      ['--color-nav-ink', '--color-nav'],
      ['--color-nav-muted', '--color-nav'],
    ];

    for (const [foreground, background] of pairs) {
      expect(contrast(token(foreground), token(background))).toBeGreaterThanOrEqual(4.5);
    }

    expect(contrast('#ffffff', token('--color-accent'))).toBeGreaterThanOrEqual(4.5);
    expect(contrast('#ffffff', token('--color-ink'))).toBeGreaterThanOrEqual(4.5);
    expect(contrast('#ffffff', token('--color-nav'))).toBeGreaterThanOrEqual(4.5);
  });
});
