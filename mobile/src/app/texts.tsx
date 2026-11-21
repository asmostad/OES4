import { router, Stack } from 'expo-router';
import { useEffect, useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Button, Card, Chips, Field, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ApiError, SearchHit, Sent, UnmatchedThread } from '@/lib/api';
import { when } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, radius, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const newKey = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

/**
 * Texts from numbers that aren't on any contact (someone who saw a yard sign, say). Reply from the company number,
 * file them on a contact (the number is added, so their next text matches), start a lead, or dismiss spam.
 */
export default function Texts() {
  const r = useCached<UnmatchedThread[]>('/api/v1/texts/unmatched', "Couldn't load the texts.");
  const [open, setOpen] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pulling, setPulling] = useState(false);
  if (!r.data) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;
  const threads = r.data;

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await r.reload(); setPulling(false); }}>
      <Stack.Screen options={{ title: 'Unknown numbers' }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Message text={notice} kind="success" />
      {threads.length === 0 && <Muted>No texts waiting. Texts from numbers that aren’t on a contact show up here.</Muted>}
      {threads.map((t) => (
        <Card key={t.number}>
          <Row last onPress={() => setOpen(open === t.number ? null : t.number)}>
            <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: space.md }}>
              <Text style={[styles.body, { fontWeight: '600' }]}>{t.display}</Text>
              <Muted>{when(t.lastAt)}</Muted>
            </View>
            <Text style={styles.body} numberOfLines={open === t.number ? undefined : 2}>{t.messages.filter((m) => m.direction === 'inbound').at(-1)?.body}</Text>
            {t.unread > 1 && open !== t.number && <Muted>{`${t.unread} texts`}</Muted>}
          </Row>
          {open === t.number && (
            <Thread thread={t} onDone={async (text) => { setNotice(text); setOpen(null); await r.reload(); }} />
          )}
        </Card>
      ))}
    </Screen>
  );
}

function Thread({ thread, onDone }: { thread: UnmatchedThread; onDone: (notice: string) => Promise<void> }) {
  const { api, me } = useApi();
  const canSend = me.permissions.includes('sms.send');
  const [reply, setReply] = useState('');
  const [mode, setMode] = useState<'reply' | 'file' | 'lead'>('reply');
  const [q, setQ] = useState('');
  const [hits, setHits] = useState<SearchHit[]>([]);
  const [first, setFirst] = useState('');
  const [last, setLast] = useState('');
  const [withLead, setWithLead] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [messages, setMessages] = useState(thread.messages);

  // Contacts to file on, as you type.
  useEffect(() => {
    if (q.trim().length < 2) return;
    let live = true;
    const t = setTimeout(() => {
      api.get<SearchHit[]>(`/api/v1/search?q=${encodeURIComponent(q.trim())}`)
        .then((h) => { if (live) setHits(h.filter((x) => x.kind === 'contact')); }).catch(() => {});
    }, 300);
    return () => { live = false; clearTimeout(t); };
  }, [api, q]);

  async function post<T>(path: string, body: unknown, what: string): Promise<T | null> {
    setBusy(what);
    setError(null);
    try {
      return await api.json<T>(path, { method: 'POST', body: JSON.stringify(body), headers: { 'Content-Type': 'application/json', 'Idempotency-Key': newKey() } });
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t go through.');
      return null;
    } finally {
      setBusy(null);
    }
  }

  async function send() {
    const sent = await post<Sent>('/api/v1/texts/unmatched/reply', { number: thread.number, body: reply.trim() }, 'reply');
    if (!sent) return;
    if (!sent.ok) { setError(sent.error ?? 'The text didn’t go through.'); return; }
    setMessages([...messages, { id: sent.messageId, direction: 'outbound', body: reply.trim(), at: new Date().toISOString(), status: 'sent' }]);
    setReply('');
  }

  async function file(contactId: string, name: string) {
    if ((await post('/api/v1/texts/unmatched/link', { number: thread.number, contactId }, 'file')) !== null) {
      await onDone(`Filed on ${name}, and the number added to them.`);
      router.push(`/contact/${contactId}`);
    }
  }

  async function lead() {
    const made = await post<{ contactId: string; opportunityId: string | null }>('/api/v1/texts/unmatched/lead',
      { number: thread.number, firstName: first.trim() || null, lastName: last.trim() || null, createOpportunity: withLead }, 'lead');
    if (!made) return;
    await onDone(made.opportunityId ? 'Contact and lead created.' : 'Contact created.');
    router.push(made.opportunityId ? `/opportunity/${made.opportunityId}` : `/contact/${made.contactId}`);
  }

  async function dismiss() {
    if ((await post('/api/v1/texts/unmatched/dismiss', { number: thread.number }, 'dismiss')) !== null) await onDone('Dismissed. A new text from that number brings it back.');
  }

  return (
    <View style={{ gap: space.sm, marginTop: space.sm }}>
      {messages.map((m) => (
        <View key={m.id} style={{ alignSelf: m.direction === 'outbound' ? 'flex-end' : 'flex-start', maxWidth: '85%' }}>
          <View style={{ backgroundColor: m.direction === 'outbound' ? colors.navy : colors.cloud, borderRadius: radius, padding: space.sm }}>
            <Text style={[styles.body, m.direction === 'outbound' && { color: colors.white }]} selectable>{m.body}</Text>
          </View>
          <Muted>{when(m.at)}</Muted>
        </View>
      ))}
      <Chips value={mode} onChange={setMode} options={[...(canSend ? [{ label: 'Reply', value: 'reply' as const }] : []),
        { label: 'File on a contact', value: 'file' as const }, { label: 'New lead', value: 'lead' as const }]} />
      {mode === 'reply' && canSend && (
        <>
          <Field label="Reply" value={reply} onChangeText={setReply} multiline style={{ minHeight: 70, textAlignVertical: 'top' }} maxLength={1600} />
          <Button title="Send reply" onPress={send} busy={busy === 'reply'} disabled={!reply.trim()} />
        </>
      )}
      {mode === 'file' && (
        <>
          {thread.possible.length > 0 && <SectionTitle>Maybe</SectionTitle>}
          {thread.possible.map((p) => <Button key={p.id} title={p.name} kind="secondary" onPress={() => file(p.id, p.name)} disabled={!!busy} />)}
          <Field label="Find a contact" value={q} onChangeText={setQ} placeholder="Name, address or email" autoCorrect={false} />
          {q.trim().length >= 2 && hits.map((h) => <Button key={h.id} title={h.subtitle ? `${h.title} · ${h.subtitle}` : h.title} kind="secondary" onPress={() => file(h.id, h.title)} disabled={!!busy} />)}
        </>
      )}
      {mode === 'lead' && (
        <>
          <View style={{ flexDirection: 'row', gap: space.sm }}>
            <View style={{ flex: 1 }}><Field label="First name" value={first} onChangeText={setFirst} autoCapitalize="words" /></View>
            <View style={{ flex: 1 }}><Field label="Last name" value={last} onChangeText={setLast} autoCapitalize="words" /></View>
          </View>
          <Chips value={withLead} onChange={setWithLead} options={[{ label: 'Contact and lead', value: true }, { label: 'Just the contact', value: false }]} />
          <Button title="Create" onPress={lead} busy={busy === 'lead'} disabled={!first.trim() && !last.trim()} />
        </>
      )}
      <Message text={error} />
      <Button title="Dismiss (spam or wrong number)" kind="secondary" onPress={dismiss} busy={busy === 'dismiss'} />
    </View>
  );
}
