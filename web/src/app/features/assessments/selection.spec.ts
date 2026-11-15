import { describe, expect, it } from 'vitest';
import { moveItem, toggleId } from './selection';

describe('assessment selection', () => {
  it('adds each checked id once and removes only the unchecked one', () => {
    expect(toggleId([], 'a', true)).toEqual(['a']);
    expect(toggleId(['a'], 'b', true)).toEqual(['a', 'b']);
    expect(toggleId(['a', 'b'], 'a', true)).toEqual(['a', 'b']);
    expect(toggleId(['a', 'b'], 'a', false)).toEqual(['b']);
  });

  it('reorders without dropping items', () => {
    expect(moveItem(['a', 'b', 'c'], 2, -1)).toEqual(['a', 'c', 'b']);
    expect(moveItem(['a', 'b'], 0, -1)).toEqual(['a', 'b']);
  });
});
