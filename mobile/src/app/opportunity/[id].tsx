import Ionicons from '@expo/vector-icons/Ionicons';
import * as ImagePicker from 'expo-image-picker';
import { Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Linking, Platform, Pressable, Text, View } from 'react-native';

import { LeadActions } from '@/components/LeadActions';
import { OfflineNote } from '@/components/OfflineNote';
import { OutboxBanner } from '@/components/OutboxBanner';
import { Button, Card, Field, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { DocumentItem, Opportunity, TimelineItem } from '@/lib/api';
import { openDocument } from '@/lib/files';
import { label, money, size, when } from '@/lib/format';
import { Outcome, outcomeMessage, useOutbox } from '@/lib/outbox';
import { useApi } from '@/lib/session';
import { colors, radius, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const PHONE_KINDS = ['mobile', 'phone', 'work', 'home', 'office'];

export default function OpportunityScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  // Each part is saved for offline; the lead's own details decide what the screen shows.
  const lead = useCached<Opportunity>(`/api/v1/opportunities/${id}`, "Couldn't load this lead.");
  const history = useCached<TimelineItem[]>(`/api/v1/records/opportunity/${id}/timeline`, "Couldn't load the timeline.");
  const files = useCached<DocumentItem[]>(`/api/v1/records/opportunity/${id}/documents?take=30`, "Couldn't load the documents.");
  const opp = lead.data;
  const timeline = history.data ?? [];
  const docs = files.data ?? [];
  const [actionError, setError] = useState<string | null>(null);
  const error = actionError ?? lead.error;
  const [notice, setNotice] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState<'note' | 'photo' | string | null>(null);

  const { reload: reloadLead } = lead;
  const { reload: reloadHistory } = history;
  const { reload: reloadFiles } = files;
  const load = useCallback(async () => {
    await Promise.all([reloadLead(), reloadHistory(), reloadFiles()]);
  }, [reloadLead, reloadHistory, reloadFiles]);

  const outbox = useOutbox();
  const base = `/api/v1/records/opportunity/${id}/`;
  const waitingHere = outbox.items.filter((i) => i.path.startsWith(base));

  /** Notes and photos go through the outbox, so they're kept and sent later when there's no signal. */
  function report(outcome: Outcome, sentText: string) {
    const m = outcomeMessage(outcome, sentText);
    if (m.kind === 'success') setNotice(m.text);
    else setError(m.text);
    return m.kind === 'success';
  }

  async function addNote() {
    if (!note.trim()) return;
    setBusy('note');
    setError(null);
    const outcome = await outbox.enqueue({
      kind: 'note', method: 'POST', path: `${base}notes`, json: { body: note.trim() }, label: `Note on ${opp?.number ?? 'a lead'}`,
    });
    if (report(outcome, 'Note added.')) setNote('');
    if (outcome.status === 'sent') await load();
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
        kind: 'photo', method: 'POST', path: `${base}photos`, file: { uri: a.uri, name, type: a.mimeType ?? 'image/jpeg' },
        label: `Photo on ${opp?.number ?? 'a lead'}`,
      });
      if (last.status === 'failed') break;
    }
    report(last, result.assets.length === 1 ? 'Photo added.' : `${result.assets.length} photos added.`);
    if (last.status === 'sent') await load();
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

  if (!opp) return <Screen>{error ? <Message text={error} /> : <Loading />}</Screen>;

  const phone = opp.contactMethods.find((m) => PHONE_KINDS.includes(m.kind));
  const email = opp.contactMethods.find((m) => m.kind === 'email');
  const digits = phone?.value.replace(/[^\d+]/g, '');
  const maps = opp.address
    ? Platform.select({ ios: `maps:0,0?q=${encodeURIComponent(opp.address)}`, android: `geo:0,0?q=${encodeURIComponent(opp.address)}`,
      default: `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(opp.address)}` })
    : null;

  return (
    <Screen refreshing={refreshing} onRefresh={async () => { setRefreshing(true); await load(); setRefreshing(false); }}>
      <Stack.Screen options={{ title: opp.number }} />
      <OfflineNote offline={lead.offline} savedAt={lead.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 20 }]}>{opp.contactName}</Text>
        <Muted>{opp.name}</Muted>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: space.sm }}>
          <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: opp.stageColor || colors.slate }} />
          <Text style={{ color: colors.slate }}>{opp.stageName}{opp.expectedValue != null ? ` · ${money(opp.expectedValue)}` : ''}</Text>
        </View>
        {opp.address && <Text style={[styles.body, { marginTop: space.sm }]}>{opp.address}</Text>}
        {opp.ownerName && <Muted>Owner: {opp.ownerName}</Muted>}
        <View style={{ flexDirection: 'row', gap: space.sm, marginTop: space.md }}>
          <Action icon="call" text="Call" onPress={digits ? () => Linking.openURL(`tel:${digits}`) : undefined} />
          <Action icon="chatbubble" text="Text" onPress={digits ? () => Linking.openURL(`sms:${digits}`) : undefined} />
          <Action icon="mail" text="Email" onPress={email ? () => Linking.openURL(`mailto:${email.value}`) : undefined} />
          <Action icon="navigate" text="Directions" onPress={maps ? () => Linking.openURL(maps) : undefined} />
        </View>
        {opp.description && <Text style={[styles.body, { marginTop: space.md }]}>{opp.description}</Text>}
      </Card>

      <Message text={error} />
      <Message text={notice} kind="success" />

      <LeadActions opp={opp} onSent={load} onMessage={(text, kind) => {
        if (kind === 'success') { setNotice(text); setError(null); } else { setError(text); setNotice(null); }
      }} />

      <SectionTitle>Add to this lead</SectionTitle>
      <Card>
        <Field label="Note" value={note} onChangeText={(t) => { setNote(t); setNotice(null); }} multiline placeholder="What happened?"
          style={{ minHeight: 80, textAlignVertical: 'top' }} />
        <Button title="Add note" onPress={addNote} busy={busy === 'note'} disabled={!note.trim()} />
        <View style={{ flexDirection: 'row', gap: space.sm, marginTop: space.md }}>
          <View style={{ flex: 1 }}><Button title="Take photo" kind="secondary" onPress={() => addPhoto('camera')} busy={busy === 'photo'} /></View>
          <View style={{ flex: 1 }}><Button title="Choose photos" kind="secondary" onPress={() => addPhoto('library')} disabled={busy === 'photo'} /></View>
        </View>
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
          <Row key={`${t.itemType}-${t.id}`} last={i === Math.min(timeline.length, 50) - 1}>
            <Muted>{[label(t.itemType === 'sms' ? 'text' : t.itemType), t.authorName, when(t.at)].filter(Boolean).join(' · ')}</Muted>
            {t.subject && <Text style={[styles.body, { fontWeight: '600' }]}>{t.subject}</Text>}
            <Text style={styles.body} numberOfLines={6}>{t.fileName ?? t.text}</Text>
          </Row>
        ))}
      </Card>
    </Screen>
  );
}

function Action({ icon, text, onPress }: { icon: React.ComponentProps<typeof Ionicons>['name']; text: string; onPress?: () => void }) {
  return (
    <Pressable onPress={onPress} disabled={!onPress} accessibilityRole="button" accessibilityLabel={text}
      style={({ pressed }) => ({ flex: 1, alignItems: 'center', paddingVertical: space.sm, borderRadius: radius,
        backgroundColor: onPress ? colors.cloud : colors.white, opacity: onPress ? (pressed ? 0.6 : 1) : 0.35 })}>
      <Ionicons name={icon} size={22} color={colors.navy} />
      <Text style={{ color: colors.navy, fontSize: 12, marginTop: 2 }}>{text}</Text>
    </Pressable>
  );
}
