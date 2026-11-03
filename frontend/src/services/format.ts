const number = new Intl.NumberFormat('en-GB');
const money = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' });
const dateTime = new Intl.DateTimeFormat('en-GB', { dateStyle: 'short', timeStyle: 'short' });

export const formatNumber = (n: number) => number.format(n);
export const formatMoney = (n: number) => money.format(n);
export const formatDateTime = (iso: string | null) => (iso ? dateTime.format(new Date(iso)) : '-');
export const shortHash = (sha: string) => (sha.length > 12 ? `${sha.slice(0, 6)}…${sha.slice(-4)}` : sha);

const delimiterNames: Record<string, string> = { ',': 'comma-separated', '|': 'pipe-separated (|)', ';': 'semicolon-separated', '\t': 'tab-separated' };
export const describeDelimiter = (d: string) => delimiterNames[d] ?? `separated by "${d}"`;
export const describeDateFormat = (f: string) => f.toLowerCase();
