const rawBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.trim();
const fallbackBase =  '/api';

const normalizedBase = (rawBase && rawBase.length > 0 ? rawBase : fallbackBase).replace(/\/$/, '');

export const apiBaseUrl = normalizedBase;

export const resolveApiUrl = (path: string) => {
  const suffix = path.startsWith('/') ? path : `/${path}`;
  return `${apiBaseUrl}${suffix}`;
};
