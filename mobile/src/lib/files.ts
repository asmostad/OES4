import { Directory, File, Paths } from 'expo-file-system';

import type { Api, DocumentItem } from './api';

/**
 * Downloads a stored file (with the sign-in token) to the app's cache and opens it in the phone's viewer
 * (Quick Look on iOS, an installed app for the type on Android).
 */
export async function openDocument(api: Api, doc: DocumentItem): Promise<void> {
  const folder = new Directory(Paths.cache, 'documents');
  if (!folder.exists) folder.create();
  const safe = doc.fileName.replace(/[^A-Za-z0-9._-]/g, '_') || 'file';
  const target = new File(folder, `${doc.id.slice(0, 8)}-${safe}`);
  const file = await File.downloadFileAsync(`${api.server}/api/v1/documents/${doc.id}`, target, {
    headers: api.authHeader(),
    idempotent: true,
  });
  if (await File.canPreview(file)) await File.preview(file, { title: doc.fileName });
  else throw new Error("There's no app on this phone that can open this kind of file.");
}
