import { Directory, File, Paths } from 'expo-file-system';

import type { Api, DocumentItem } from './api';

/**
 * Downloads a stored file (with the sign-in token) to the app's cache and opens it in the phone's viewer
 * (Quick Look on iOS, an installed app for the type on Android).
 */
export async function openDocument(api: Api, doc: DocumentItem): Promise<void> {
  await openFile(api, `/api/v1/documents/${doc.id}`, doc.fileName, doc.id.slice(0, 8));
}

/**
 * Downloads any file the API serves (a stored document, or a PDF made on request like a change order) and opens it.
 * A refusal (no PDF maker on the server, say) comes back as the server's reason.
 */
export async function openFile(api: Api, path: string, fileName: string, tag = Date.now().toString(36)): Promise<void> {
  const folder = new Directory(Paths.cache, 'documents');
  if (!folder.exists) folder.create();
  const safe = fileName.replace(/[^A-Za-z0-9._-]/g, '_') || 'file';
  const target = new File(folder, `${tag}-${safe}`);
  let file: File;
  try {
    file = await File.downloadFileAsync(`${api.server}${path}`, target, { headers: api.authHeader(), idempotent: true });
  } catch {
    // The download only says it failed; ask again for the reason.
    await api.send(path, { headers: { Accept: '*/*' } });
    throw new Error("Couldn't download the file. Try again.");
  }
  if (await File.canPreview(file)) await File.preview(file, { title: fileName });
  else throw new Error("There's no app on this phone that can open this kind of file.");
}
