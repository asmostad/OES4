import { Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { MessageFull } from '@/lib/api';
import { openDocument } from '@/lib/files';
import { label, when } from '@/lib/format';
import { useApi } from '@/lib/session';
import { useCached } from '@/lib/useCached';

/** One text or email in full, with its attachments. Saved for offline. */
export default function MessageScreen() {
  const { channel, id } = useLocalSearchParams<{ channel: string; id: string }>();
  const { api } = useApi();
  const r = useCached<MessageFull>(`/api/v1/messages/${channel}/${id}`, "Couldn't load this message.");
  const [error, setError] = useState<string | null>(null);
  const m = r.data;
  if (!m) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;
  const inbound = m.direction === 'inbound';

  async function open(doc: { documentId: string; fileName: string }) {
    setError(null);
    try {
      const type = /\.pdf$/i.test(doc.fileName) ? 'application/pdf' : /\.(jpe?g|png|heic)$/i.test(doc.fileName) ? 'image/jpeg' : 'application/octet-stream';
      await openDocument(api, { id: doc.documentId, fileName: doc.fileName, contentType: type, sizeBytes: 0, category: null, createdAt: m!.at });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Couldn't open the file.");
    }
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: m.channel === 'sms' ? 'Text' : 'Email' }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        {m.subject && <Text style={[styles.title, { fontSize: 18 }]}>{m.subject}</Text>}
        <Muted>{inbound ? `From ${m.counterparty}` : `To ${m.counterparty}${m.senderName ? ` · sent by ${m.senderName}` : ''}`}</Muted>
        {m.cc && <Muted>Cc {m.cc}</Muted>}
        <Muted>{[when(m.at), m.status !== 'sent' && m.status !== 'received' ? label(m.status) : null].filter(Boolean).join(' · ')}</Muted>
        {m.error && <Message text={m.error} />}
        <View style={{ marginTop: 12 }}><Text style={styles.body} selectable>{m.body}</Text></View>
      </Card>
      <Message text={error} />
      {m.attachments.length > 0 && (
        <>
          <SectionTitle>Attachments</SectionTitle>
          <Card>
            {m.attachments.map((a, i) => (
              <Row key={a.documentId} last={i === m.attachments.length - 1} onPress={() => open(a)}>
                <Text style={styles.body}>{a.fileName}</Text>
              </Row>
            ))}
          </Card>
        </>
      )}
    </Screen>
  );
}
