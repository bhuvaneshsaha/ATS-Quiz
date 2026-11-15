import { describe, expect, it } from 'vitest';
import { buildQuestionContent, emptyDraft } from './question-content';

describe('question content', () => {
  it('builds each of the six types', () => {
    const single = emptyDraft();
    single.prompt = 'Pick';
    single.options = [
      { id: 'a', text: 'Yes', correct: true },
      { id: 'b', text: 'No', correct: false },
    ];
    expect(buildQuestionContent(single)).toMatchObject({
      content: { type: 'single_choice', prompt: 'Pick' },
    });

    const multiple = { ...single, type: 'multiple_choice', partialCredit: 'none' };
    multiple.options = [
      { id: 'a', text: 'Yes', correct: true },
      { id: 'b', text: 'Also', correct: true },
    ];
    expect(buildQuestionContent(multiple)).toMatchObject({
      content: { type: 'multiple_choice', partialCredit: 'none' },
    });

    expect(
      buildQuestionContent({ ...emptyDraft(), type: 'numeric', prompt: 'How many', correctValue: '8', tolerance: '0' }),
    ).toMatchObject({ content: { type: 'numeric', correctValue: 8, tolerance: 0 } });

    expect(buildQuestionContent({ ...emptyDraft(), type: 'long_answer', prompt: 'Explain', rubric: 'Clarity' })).toMatchObject({
      content: { type: 'long_answer', rubric: 'Clarity' },
    });

    expect(
      buildQuestionContent({ ...emptyDraft(), type: 'ordering', prompt: 'Order', items: ['One', 'Two'] }),
    ).toMatchObject({ content: { type: 'ordering' } });

    expect(
      buildQuestionContent({
        ...emptyDraft(),
        type: 'matching',
        prompt: 'Match',
        pairs: [
          { left: 'A', right: '1' },
          { left: 'B', right: '2' },
        ],
      }),
    ).toMatchObject({ content: { type: 'matching' } });
  });

  it('rejects a blank prompt and a single-choice question with two correct options', () => {
    expect(buildQuestionContent(emptyDraft())).toEqual({ error: 'A question needs a prompt.' });
    const draft = emptyDraft();
    draft.prompt = 'Pick';
    draft.options = [
      { id: 'a', text: 'Yes', correct: true },
      { id: 'b', text: 'Also', correct: true },
    ];
    expect(buildQuestionContent(draft)).toEqual({ error: 'Mark exactly one correct option.' });
  });
});
