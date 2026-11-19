export interface AnswerValue {
  optionId?: string;
  optionIds?: string[];
  value?: number;
  text?: string;
  itemIds?: string[];
  pairs?: { leftId: string; rightId: string }[];
}

export function responseFor(type: string, value: AnswerValue): Record<string, unknown> | null {
  switch (type) {
    case 'single_choice':
      return value.optionId ? { optionId: value.optionId } : null;
    case 'multiple_choice':
      return value.optionIds && value.optionIds.length > 0 ? { optionIds: value.optionIds } : null;
    case 'numeric':
      return typeof value.value === 'number' && !Number.isNaN(value.value) ? { value: value.value } : null;
    case 'long_answer':
      return value.text && value.text.trim() ? { text: value.text } : null;
    case 'ordering':
      return value.itemIds && value.itemIds.length > 0 ? { itemIds: value.itemIds } : null;
    case 'matching':
      return value.pairs && value.pairs.length > 0 ? { pairs: value.pairs } : null;
    default:
      return null;
  }
}

export function answerFromResponse(response: unknown): AnswerValue {
  if (!response || typeof response !== 'object') {
    return {};
  }
  const value = response as AnswerValue;
  return {
    optionId: typeof value.optionId === 'string' ? value.optionId : undefined,
    optionIds: Array.isArray(value.optionIds) ? value.optionIds.filter((id) => typeof id === 'string') : undefined,
    value: typeof value.value === 'number' ? value.value : undefined,
    text: typeof value.text === 'string' ? value.text : undefined,
    itemIds: Array.isArray(value.itemIds) ? value.itemIds.filter((id) => typeof id === 'string') : undefined,
    pairs: Array.isArray(value.pairs)
      ? value.pairs.filter(
          (pair) => pair && typeof pair.leftId === 'string' && typeof pair.rightId === 'string',
        )
      : undefined,
  };
}

export function readableAnswer(responseJson: string): string {
  try {
    const value = JSON.parse(responseJson) as AnswerValue;
    if (typeof value.text === 'string' && value.text.trim()) {
      return value.text;
    }
    if (typeof value.value === 'number') {
      return String(value.value);
    }
    if (Array.isArray(value.pairs) && value.pairs.length > 0) {
      return `${value.pairs.length} pairs matched`;
    }
    if (Array.isArray(value.itemIds) && value.itemIds.length > 0) {
      return 'Order submitted';
    }
    if (Array.isArray(value.optionIds)) {
      return `${value.optionIds.length} options selected`;
    }
    if (typeof value.optionId === 'string') {
      return 'One option selected';
    }
  } catch {
    return responseJson;
  }
  return responseJson;
}

export function typeLabel(type: string): string {
  switch (type) {
    case 'single_choice':
      return 'Single choice';
    case 'multiple_choice':
      return 'Multiple choice';
    case 'numeric':
      return 'Numeric';
    case 'long_answer':
      return 'Written answer';
    case 'ordering':
      return 'Ordering';
    case 'matching':
      return 'Matching';
    default:
      return type.replaceAll('_', ' ');
  }
}
