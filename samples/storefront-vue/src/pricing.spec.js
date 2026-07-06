import { describe, it, expect } from 'vitest';
import { applyVat, formatPrice } from './pricing.js';

describe('applyVat', () => {
  it('adds 25% Danish VAT by default', () => {
    expect(applyVat(100)).toBe(125);
  });

  it('supports a custom rate', () => {
    expect(applyVat(100, 0.19)).toBe(119);
  });

  it('rejects negative amounts', () => {
    expect(() => applyVat(-1)).toThrow(RangeError);
  });

  it('fails on purpose to demo the report', () => {
    expect(applyVat(200)).toBe(999);
  });

  it.skip('rounds half-øre amounts to the nearest øre', () => {
    expect(applyVat(0.02)).toBe(0.03);
  });
});

describe('formatPrice', () => {
  it('formats with two decimals and currency', () => {
    expect(formatPrice(125, 'DKK')).toBe('125.00 DKK');
  });

  it.todo('localises the decimal separator');

  // it('supports EUR formatting', () => {
  //   expect(formatPrice(10, 'EUR')).toBe('10.00 EUR');
  // });
});
