import { describe, expect, it } from 'vitest';
import { answerFromResponse, readableAnswer, responseFor } from './paper';

describe('answer payloads', () => {
  it('matches the shapes the scorer reads', () => {
    expect(responseFor('single_choice', { optionId: 'a' })).toEqual({ optionId: 'a' });
    expect(responseFor('multiple_choice', { optionIds: ['a', 'b'] })).toEqual({ optionIds: ['a', 'b'] });
    expect(responseFor('multiple_choice', { optionIds: [] })).toBeNull();
    expect(responseFor('numeric', { value: 8 })).toEqual({ value: 8 });
    expect(responseFor('long_answer', { text: '  logs  ' })).toEqual({ text: '  logs  ' });
    expect(responseFor('long_answer', { text: '   ' })).toBeNull();
    expect(responseFor('ordering', { itemIds: ['a', 'b'] })).toEqual({ itemIds: ['a', 'b'] });
    expect(responseFor('matching', { pairs: [{ leftId: 'l', rightId: 'r' }] })).toEqual({
      pairs: [{ leftId: 'l', rightId: 'r' }],
    });
  });

  it('restores a saved response and hides raw json from graders', () => {
    expect(answerFromResponse({ text: 'Because', optionId: 1 }).text).toBe('Because');
    expect(readableAnswer('{"text":"I would read the logs."}')).toBe('I would read the logs.');
    expect(readableAnswer('{"value":8}')).toBe('8');
  });
});
