import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useRef, useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Platform, ScrollView, Text, TextInput, View } from 'react-native';

import { Button, Message, Muted, styles } from '@/components/ui';
import { ApiError, Conversation } from '@/lib/api';
import { routeFor } from '@/lib/routes';
import { useApi } from '@/lib/session';
import { colors, radius, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const newKey = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

/**
 * One conversation with the assistant. The answer comes back whole (a lookup can take a few seconds). A dropped
 * connection retries with the same key, so the question is never asked, or charged, twice.
 */
export default function Chat() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  const r = useCached<Conversation>(`/api/v1/assistant/conversations/${id}`, "Couldn't load this conversation.");
  const [sent, setSent] = useState<Conversation | null>(null);
  const [text, setText] = useState('');
  const [pending, setPending] = useState<{ text: string; key: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const scroll = useRef<React.ComponentRef<typeof ScrollView>>(null);
  const c = sent ?? r.data;

  async function send(retry?: { text: string; key: string }) {
    const ask = retry ?? { text: text.trim(), key: newKey() };
    if (!ask.text) return;
    setPending(ask);
    setError(null);
    setText('');
    try {
      const reply = await api.json<Conversation>(`/api/v1/assistant/conversations/${id}/messages`, {
        method: 'POST', body: JSON.stringify({ text: ask.text }), headers: { 'Content-Type': 'application/json', 'Idempotency-Key': ask.key },
      });
      setSent(reply);
      setPending(null);
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t go through.');
      // Keep it so "Try again" resends with the same key.
      if (!(x instanceof ApiError) || x.status === 0) return;
      setPending(null);
      setText(ask.text);
    }
  }

  const record = c?.recordUrl ? routeFor(c.recordUrl) : null;
  const lines = c?.lines.filter((l) => l.text.trim()) ?? [];

  return (
    <KeyboardAvoidingView style={{ flex: 1, backgroundColor: colors.cloud }} behavior={Platform.OS === 'ios' ? 'padding' : undefined} keyboardVerticalOffset={90}>
      <Stack.Screen options={{ title: c?.title ?? 'Assistant' }} />
      <ScrollView ref={scroll} contentContainerStyle={{ padding: space.lg, gap: space.md }} onContentSizeChange={() => scroll.current?.scrollToEnd({ animated: true })}>
        {!c && (r.error ? <Message text={r.error} /> : <ActivityIndicator />)}
        {c?.recordLabel && (
          <Text style={{ color: colors.navy }} onPress={record ? () => router.push(record) : undefined} accessibilityRole={record ? 'link' : undefined}>
            {`About ${c.recordLabel}`}
          </Text>
        )}
        {c && lines.length === 0 && !pending && <Muted>Ask anything about your leads, jobs, tasks or schedule.</Muted>}
        {lines.map((l, i) => <Bubble key={i} mine={l.role === 'user'} text={l.text} note={l.tools.length ? `Looked up: ${[...new Set(l.tools)].join(', ')}` : null} />)}
        {pending && <Bubble mine text={pending.text} />}
        {pending && !error && <View style={{ flexDirection: 'row', gap: space.sm, alignItems: 'center' }}><ActivityIndicator /><Muted>Thinking…</Muted></View>}
        <Message text={error} />
        {pending && error && <Button title="Try again" kind="secondary" onPress={() => send(pending)} />}
      </ScrollView>
      <View style={{ flexDirection: 'row', gap: space.sm, padding: space.md, borderTopWidth: 1, borderColor: colors.border, backgroundColor: colors.white }}>
        <TextInput value={text} onChangeText={setText} placeholder="Ask the assistant" placeholderTextColor={colors.muted} multiline
          style={[styles.input, { flex: 1, maxHeight: 120 }]} accessibilityLabel="Message" editable={!pending} />
        <View style={{ justifyContent: 'flex-end' }}><Button title="Send" onPress={() => send()} disabled={!text.trim() || !!pending} /></View>
      </View>
    </KeyboardAvoidingView>
  );
}

function Bubble({ mine, text, note }: { mine?: boolean; text: string; note?: string | null }) {
  return (
    <View style={{ alignSelf: mine ? 'flex-end' : 'flex-start', maxWidth: '88%' }}>
      <View style={{ backgroundColor: mine ? colors.navy : colors.white, borderRadius: radius, padding: space.md }}>
        <Text style={[styles.body, mine && { color: colors.white }]} selectable>{text}</Text>
      </View>
      {note && <Muted>{note}</Muted>}
    </View>
  );
}
