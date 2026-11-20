import type { Api, DocumentItem } from './api';

/** In the browser (testing): fetch with the token and open the file in a new tab. */
export async function openDocument(api: Api, doc: DocumentItem): Promise<void> {
  const res = await api.send(`/api/v1/documents/${doc.id}`, { headers: { Accept: '*/*' } });
  const url = URL.createObjectURL(await res.blob());
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
