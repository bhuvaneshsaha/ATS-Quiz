import { describe, expect, it } from 'vitest';
import { filterOptions } from './combobox';

describe('combobox filter', () => {
  const options = [
    { value: '1', label: 'Software engineering', hint: 'Function' },
    { value: '2', label: 'Product', hint: 'Department' },
    { value: '3', label: 'Hiring screen' },
  ];

  it('returns every option when the query is blank', () => {
    expect(filterOptions(options, '  ')).toHaveLength(3);
  });

  it('matches the label or the hint and is case insensitive', () => {
    expect(filterOptions(options, 'soft').map((option) => option.value)).toEqual(['1']);
    expect(filterOptions(options, 'DEPARTMENT').map((option) => option.value)).toEqual(['2']);
  });

  it('returns nothing when nothing matches', () => {
    expect(filterOptions(options, 'zzzz')).toEqual([]);
  });
});
