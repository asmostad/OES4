import { router, Stack } from 'expo-router';
import { useState } from 'react';
import { Text } from 'react-native';

import { Button, Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ApiError, AssistantHome } from '@/lib/api';
import { when } from '@/lib/format';
import { useApi } from '@/lib/session';
import { useCached } from '@/lib/useCached';

const newKey = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

/** The assistant: your conversations, and a new one. It looks things up as you, so it only sees what you can. */
export default function AssistantList() {
  const { api } = useApi();
  const home = useCached<AssistantHome>('/api/v1/assistant', "Couldn't load the assistant.");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pulling, setPulling] = useState(false);
  const h = home.data;
  if (!h) return <Screen>{home.error ? <Message text={home.error} /> : <Loading />}</Screen>;

  async function start() {
    setBusy(true);
    setError(null);
    try {
      const { id } = await api.json<{ id: string }>('/api/v1/assistant/conversations', {
        method: 'POST', body: JSON.stringify({}), headers: { 'Content-Type': 'application/json', 'Idempotency-Key': newKey() },
      });
      router.push(`/assistant/${id}`);
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : "Couldn't start a conversation.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await home.reload(); setPulling(false); }}>
      <Stack.Screen options={{ title: 'Assistant' }} />
      {!h.configured && <Message text="The assistant isn't connected for your company yet. An admin sets it up on the web." />}
      <Message text={error} />
      {h.configured && <Button title="New conversation" onPress={start} busy={busy} />}
      <Muted>Ask about your leads, jobs, tasks and schedule. It only sees what you can see.</Muted>
      {h.conversations.length > 0 && (
        <>
          <SectionTitle>Recent</SectionTitle>
          <Card>
            {h.conversations.map((c, i) => (
              <Row key={c.id} last={i === h.conversations.length - 1} onPress={() => router.push(`/assistant/${c.id}`)}>
                <Text style={styles.body} numberOfLines={1}>{c.title}</Text>
                <Muted>{when(c.lastMessageAt)}</Muted>
              </Row>
            ))}
          </Card>
        </>
      )}
    </Screen>
  );
}
