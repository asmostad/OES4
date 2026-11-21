import Ionicons from '@expo/vector-icons/Ionicons';
import * as ImagePicker from 'expo-image-picker';
import { router } from 'expo-router';
import { type ReactNode, useCallback, useState } from 'react';
import { Text, View } from 'react-native';

import { OutboxBanner } from '@/components/OutboxBanner';
import { Button, Card, Field, Message, Muted, Row, SectionTitle, styles } from '@/components/ui';
import { DocumentItem, TimelineItem } from '@/lib/api';
import { openDocument } from '@/lib/files';
import { label, size, when } from '@/lib/format';
import { Outcome, outcomeMessage, useOutbox } from '@/lib/outbox';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/**
 * The bottom half of a record screen (lead, project, trade): add a note or photos, what's waiting to send,
 * documents and the timeline. Notes and photos go through the outbox, so they're kept and sent later
 * when there's no signal. Returns `reload` so the screen's pull-to-refresh can include it.
 */
export function useRecordFeed(entityType: string, id: string, what: string, addTitle: string): { reload: () => Promise<void>; view: ReactNode } {
  const { api, me } = useApi();
  const canText = me.permissions.includes('sms.send');
  const canEmail = me.permissions.includes('email.send');
  const compose = (channel: 'sms' | 'email') =>
    router.push({ pathname: '/compose', params: { entityType, entityId: id, channel, label: what } });
  const history = useCached<TimelineItem[]>(`/api/v1/records/${entityType}/${id}/timeline`, "Couldn't load the timeline.");
  const files = useCached<DocumentItem[]>(`/api/v1/records/${entityType}/${id}/documents?take=30`, "Couldn't load the documents.");
  const timeline = history.data ?? [];
  const docs = files.data ?? [];
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState<string | null>(null);

  const { reload: reloadHistory } = history;
  const { reload: reloadFiles } = files;
  const reload = useCallback(async () => { await Promise.all([reloadHistory(), reloadFiles()]); }, [reloadHistory, reloadFiles]);

  const outbox = useOutbox();
  const base = `/api/v1/records/${entityType}/${id}/`;
  const waitingHere = outbox.items.filter((i) => i.path.startsWith(base));

  function report(outcome: Outcome, sentText: string) {
    const m = outcomeMessage(outcome, sentText);
    if (m.kind === 'success') { setNotice(m.text); setError(null); } else { setError(m.text); setNotice(null); }
    return m.kind === 'success';
  }

  async function addNote() {
    if (!note.trim()) return;
    setBusy('note');
    const outcome = await outbox.enqueue({ kind: 'note', method: 'POST', path: `${base}notes`, json: { body: note.trim() }, label: `Note on ${what}` });
    if (report(outcome, 'Note added.')) setNote('');
    if (outcome.status === 'sent') await reload();
    setBusy(null);
  }

  async function addPhoto(source: 'camera' | 'library') {
    setError(null);
    const permission = source === 'camera' ? await ImagePicker.requestCameraPermissionsAsync() : await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) return setError(source === 'camera' ? 'Allow camera access in Settings to take photos.' : 'Allow photo access in Settings to add photos.');
    const result = source === 'camera'
      ? await ImagePicker.launchCameraAsync({ mediaTypes: ['images'], quality: 0.7 })
      : await ImagePicker.launchImageLibraryAsync({ mediaTypes: ['images'], quality: 0.7, allowsMultipleSelection: true, selectionLimit: 10 });
    if (result.canceled || !result.assets?.length) return;
    setBusy('photo');
    let last: Outcome = { status: 'sent' };
    for (const [i, a] of result.assets.entries()) {
      const name = a.fileName ?? `photo-${Date.now()}-${i + 1}.jpg`;
      last = await outbox.enqueue({
        kind: 'photo', method: 'POST', path: `${base}photos`, file: { uri: a.uri, name, type: a.mimeType ?? 'image/jpeg' }, label: `Photo on ${what}`,
      });
      if (last.status === 'failed') break;
    }
    report(last, result.assets.length === 1 ? 'Photo added.' : `${result.assets.length} photos added.`);
    if (last.status === 'sent') await reload();
    setBusy(null);
  }

  async function open(doc: DocumentItem) {
    setBusy(doc.id);
    try {
      await openDocument(api, doc);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Couldn't open the file.");
    } finally {
      setBusy(null);
    }
  }

  const view = (
    <>
      <SectionTitle>{addTitle}</SectionTitle>
      <Message text={error} />
      <Message text={notice} kind="success" />
      <Card>
        <Field label="Note" value={note} onChangeText={(t) => { setNote(t); setNotice(null); }} multiline placeholder="What happened?"
          style={{ minHeight: 80, textAlignVertical: 'top' }} />
        <Button title="Add note" onPress={addNote} busy={busy === 'note'} disabled={!note.trim()} />
        <View style={{ flexDirection: 'row', gap: space.sm, marginTop: space.md }}>
          <View style={{ flex: 1 }}><Button title="Take photo" kind="secondary" onPress={() => addPhoto('camera')} busy={busy === 'photo'} /></View>
          <View style={{ flex: 1 }}><Button title="Choose photos" kind="secondary" onPress={() => addPhoto('library')} disabled={busy === 'photo'} /></View>
        </View>
        {(canText || canEmail) && (
          <View style={{ flexDirection: 'row', gap: space.sm, marginTop: space.md }}>
            {canText && <View style={{ flex: 1 }}><Button title="Send a text" kind="secondary" onPress={() => compose('sms')} /></View>}
            {canEmail && <View style={{ flex: 1 }}><Button title="Send an email" kind="secondary" onPress={() => compose('email')} /></View>}
          </View>
        )}
      </Card>

      <OutboxBanner />
      {waitingHere.length > 0 && (
        <Card>
          {waitingHere.map((w, i) => (
            <Row key={w.key} last={i === waitingHere.length - 1}>
              <View style={{ flexDirection: 'row', gap: space.md, alignItems: 'center' }}>
                <Ionicons name={w.kind === 'photo' ? 'image-outline' : 'document-text-outline'} size={20} color={w.error ? colors.danger : colors.warning} />
                <View style={{ flex: 1 }}>
                  <Text style={styles.body} numberOfLines={2}>{w.kind === 'note' ? (w.json as { body: string }).body : w.file?.name}</Text>
                  <Muted>{w.error ? `Couldn’t be sent: ${w.error}` : 'Waiting to send'}</Muted>
                </View>
              </View>
            </Row>
          ))}
        </Card>
      )}

      <SectionTitle>Documents</SectionTitle>
      <Card>
        {docs.length === 0 && <Muted>No files yet.</Muted>}
        {docs.map((d, i) => (
          <Row key={d.id} last={i === docs.length - 1} onPress={() => open(d)}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
              <Ionicons name={d.contentType === 'application/pdf' ? 'document-text-outline' : d.contentType.startsWith('image/') ? 'image-outline' : 'document-outline'}
                size={22} color={colors.navy} />
              <View style={{ flex: 1 }}>
                <Text style={styles.body} numberOfLines={1}>{d.fileName}</Text>
                <Muted>{[label(d.category), size(d.sizeBytes), when(d.createdAt)].filter(Boolean).join(' · ')}</Muted>
              </View>
              {busy === d.id && <Muted>Opening…</Muted>}
            </View>
          </Row>
        ))}
      </Card>

      <SectionTitle>Timeline</SectionTitle>
      <Card>
        {timeline.length === 0 && <Muted>Nothing here yet.</Muted>}
        {timeline.slice(0, 50).map((t, i) => (
          <Row key={`${t.itemType}-${t.id}`} last={i === Math.min(timeline.length, 50) - 1}
            onPress={t.itemType === 'sms' || t.itemType === 'email' ? () => router.push(`/message/${t.itemType}/${t.id}`) : undefined}>
            <Muted>{[label(t.itemType === 'sms' ? 'text' : t.itemType), t.authorName, when(t.at)].filter(Boolean).join(' · ')}</Muted>
            {t.subject && <Text style={[styles.body, { fontWeight: '600' }]}>{t.subject}</Text>}
            <Text style={styles.body} numberOfLines={6}>{t.fileName ?? t.text}</Text>
          </Row>
        ))}
      </Card>
    </>
  );
  return { reload, view };
}
