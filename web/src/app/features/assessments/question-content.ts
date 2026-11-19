export interface DraftOption {
  id: string;
  text: string;
  correct: boolean;
}

export interface QuestionDraft {
  type: string;
  prompt: string;
  points: number;
  options: DraftOption[];
  partialCredit: string;
  correctValue: string;
  tolerance: string;
  rubric: string;
  items: string[];
  pairs: { left: string; right: string }[];
}

export function emptyDraft(): QuestionDraft {
  return {
    type: 'single_choice',
    prompt: '',
    points: 4,
    options: [
      { id: crypto.randomUUID(), text: '', correct: true },
      { id: crypto.randomUUID(), text: '', correct: false },
    ],
    partialCredit: 'none',
    correctValue: '',
    tolerance: '0',
    rubric: '',
    items: ['', ''],
    pairs: [
      { left: '', right: '' },
      { left: '', right: '' },
    ],
  };
}

export function buildQuestionContent(draft: QuestionDraft): { content: object } | { error: string } {
  const prompt = draft.prompt.trim();
  if (!prompt) {
    return { error: 'A question needs a prompt.' };
  }
  if (!Number.isFinite(draft.points) || draft.points <= 0) {
    return { error: 'Points must be greater than zero.' };
  }

  const attachments: never[] = [];
  switch (draft.type) {
    case 'single_choice':
    case 'multiple_choice': {
      const options = draft.options
        .map((option) => ({ id: option.id, text: option.text.trim(), isCorrect: option.correct }))
        .filter((option) => option.text);
      if (options.length < 2) {
        return { error: 'Add at least two options.' };
      }
      const correct = options.filter((option) => option.isCorrect).length;
      if (draft.type === 'single_choice' && correct !== 1) {
        return { error: 'Mark exactly one correct option.' };
      }
      if (draft.type === 'multiple_choice' && correct < 1) {
        return { error: 'Mark at least one correct option.' };
      }
      return draft.type === 'single_choice'
        ? { content: { type: 'single_choice', prompt, attachments, options } }
        : {
            content: {
              type: 'multiple_choice',
              prompt,
              attachments,
              options,
              partialCredit: draft.partialCredit,
            },
          };
    }
    case 'numeric': {
      const correctValue = Number(draft.correctValue);
      const tolerance = Number(draft.tolerance || '0');
      if (!Number.isFinite(correctValue)) {
        return { error: 'Enter the correct number.' };
      }
      if (!Number.isFinite(tolerance) || tolerance < 0) {
        return { error: 'Tolerance cannot be negative.' };
      }
      return { content: { type: 'numeric', prompt, attachments, correctValue, tolerance } };
    }
    case 'long_answer':
      return { content: { type: 'long_answer', prompt, attachments, rubric: draft.rubric.trim() } };
    case 'ordering': {
      const items = draft.items
        .map((text) => text.trim())
        .filter(Boolean)
        .map((text) => ({ id: crypto.randomUUID(), text }));
      if (items.length < 2) {
        return { error: 'An ordering question needs at least two items.' };
      }
      return {
        content: { type: 'ordering', prompt, attachments, items, partialCredit: draft.partialCredit },
      };
    }
    case 'matching': {
      const pairs = draft.pairs
        .map((pair) => ({ id: crypto.randomUUID(), left: pair.left.trim(), right: pair.right.trim() }))
        .filter((pair) => pair.left && pair.right);
      if (pairs.length < 2) {
        return { error: 'A matching question needs at least two pairs.' };
      }
      return {
        content: { type: 'matching', prompt, attachments, pairs, partialCredit: draft.partialCredit },
      };
    }
    default:
      return { error: 'Choose a question type.' };
  }
}
