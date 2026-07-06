export function applyVat(amount, rate = 0.25) {
  if (amount < 0) throw new RangeError('amount must be non-negative');
  return Math.round(amount * (1 + rate) * 100) / 100;
}

export function formatPrice(amount, currency = 'DKK') {
  return `${amount.toFixed(2)} ${currency}`;
}
