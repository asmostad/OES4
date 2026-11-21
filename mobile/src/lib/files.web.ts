import type { Api, DocumentItem } from './api';

/** In the browser (testing): fetch with the token and open the file in a new tab. */
export async function openDocument(api: Api, doc: DocumentItem): Promise<void> {
  await openFile(api, `/api/v1/documents/${doc.id}`, doc.fileName);
}

export async function openFile(api: Api, path: string, fileName: string): Promise<void> {
  void fileName;
  const res = await api.send(path, { headers: { Accept: '*/*' } });
  const url = URL.createObjectURL(await res.blob());
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
