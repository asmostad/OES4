import { router, Stack } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { Pressable, Text, View } from 'react-native';

import { ContactMethods, openMaps } from '@/components/Records';
import { Button, Card, Chips, Field, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ApiError, Campaign, Disposition, ProspectCard, QueueCounts } from '@/lib/api';
import { todayIso, when } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, radius, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const CALL_BACK = [
  { label: 'Automatic', value: 0 }, { label: 'Tomorrow', value: 1 }, { label: 'In 3 days', value: 3 }, { label: 'Next week', value: 7 },
];

const failed = (e: unknown, fallback: string) => (e instanceof ApiError && e.status !== 0 ? e.message : fallback);

/**
 * The prospecting call list: claim the next due lead (held for you for a few minutes so no one else calls them),
 * call, record how it went, and the next one comes up. Needs a signal: the list is shared with the office.
 */
export default function Calls() {
  const { api } = useApi();
  const [campaignId, setCampaignId] = useState<string | null>(null);
  const [mine, setMine] = useState(false);
  const [card, setCard] = useState<ProspectCard | null>(null);
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const skipped = useRef<string[]>([]);
  const current = useRef<string | null>(null);

  const filter = new URLSearchParams({ mine: String(mine) });
  if (campaignId) filter.set('campaignId', campaignId);
  const campaigns = useCached<Campaign[]>('/api/v1/campaigns', "Couldn't load campaigns.");
  const counts = useCached<QueueCounts>(`/api/v1/prospecting/counts?${filter}`, "Couldn't load the list.");

  // Leaving the screen mid-call hands the lead back so someone else can call them.
  useEffect(() => () => {
    if (current.current) api.post(`/api/v1/prospecting/${current.current}/release`, {}).catch(() => {});
  }, [api]);

  async function next() {
    setBusy(true);
    setError(null);
    try {
      const q = new URLSearchParams(filter);
      skipped.current.forEach((id) => q.append('skip', id));
      const claimed = await api.post<{ opportunityId: string } | undefined>(`/api/v1/prospecting/next?${q}`, {});
      if (!claimed) {
        current.current = null;
        setCard(null);
        setDone(true);
        counts.reload();
        return;
      }
      current.current = claimed.opportunityId;
      setCard(await api.get<ProspectCard>(`/api/v1/prospecting/${claimed.opportunityId}`));
    } catch (e) {
      setError(failed(e, "Couldn't reach the server. The call list needs a signal."));
    } finally {
      setBusy(false);
    }
  }

  async function stop() {
    const id = current.current;
    current.current = null;
    setCard(null);
    skipped.current = [];
    if (id) await api.post(`/api/v1/prospecting/${id}/release`, {}).catch(() => {});
    counts.reload();
  }

  function skip() {
    if (card) skipped.current.push(card.id);
    next();
  }

  if (card) return <Calling card={card} busy={busy} error={error} onSaved={next} onSkip={skip} onStop={stop} />;

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Call list' }} />
      {(campaigns.data?.length ?? 0) > 0 && (
        <>
          <SectionTitle>Campaign</SectionTitle>
          <Chips scroll value={campaignId} onChange={(v) => { setCampaignId(v); setDone(false); }}
            options={[{ label: 'All leads', value: null }, ...campaigns.data!.map((c) => ({ label: c.name, value: c.id as string | null }))]} />
        </>
      )}
      <Chips value={mine} onChange={(v) => { setMine(v); setDone(false); }}
        options={[{ label: 'Everyone I can see', value: false }, { label: 'Mine', value: true }]} />
      <Card>
        {counts.data ? (
          <View style={{ flexDirection: 'row', justifyContent: 'space-around' }}>
            <Count n={counts.data.due} label="Due now" />
            <Count n={counts.data.beingWorked} label="Being called" />
            <Count n={counts.data.callBacksLater} label="Call back later" />
          </View>
        ) : counts.error ? <Message text={counts.error} /> : <Loading />}
      </Card>
      {done && <Message kind="success" text="You're all caught up. Nothing else is due on this list." />}
      <Message text={error} />
      <Button title={done ? 'Check again' : 'Start calling'} busy={busy}
        onPress={() => { skipped.current = []; setDone(false); next(); }} />
      <Muted>Each lead is held for you for a few minutes so no one else calls them at the same time.</Muted>
    </Screen>
  );
}

function Count({ n, label }: { n: number; label: string }) {
  return (
    <View style={{ alignItems: 'center' }}>
      <Text style={{ fontSize: 26, fontWeight: '700', color: colors.navy }}>{n}</Text>
      <Muted>{label}</Muted>
    </View>
  );
}

function Calling({ card, busy, error, onSaved, onSkip, onStop }: {
  card: ProspectCard; busy: boolean; error: string | null; onSaved: () => void; onSkip: () => void; onStop: () => void;
}) {
  const { api } = useApi();
  const outcomes = useCached<Disposition[]>('/api/v1/prospecting/dispositions', "Couldn't load call outcomes.");
  const [outcome, setOutcome] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const [days, setDays] = useState(0);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  async function save() {
    if (!outcome) { setSaveError('Pick how the call went.'); return; }
    setSaving(true);
    setSaveError(null);
    try {
      await api.post(`/api/v1/prospecting/${card.id}/disposition`,
        { dispositionId: outcome, note: note.trim() || null, callBackOn: days ? todayIso(days) : null });
      setOutcome(null);
      setNote('');
      setDays(0);
      onSaved();
    } catch (e) {
      setSaveError(failed(e, "Couldn't save. Check your signal and try again."));
    } finally {
      setSaving(false);
    }
  }

  const tries = card.attempts ? `${card.attempts} attempt${card.attempts === 1 ? '' : 's'}` : 'never called';
  return (
    <Screen>
      <Stack.Screen options={{ title: 'Calling' }} />
      <Card>
        <Text style={[styles.title, { fontSize: 20 }]} onPress={() => router.push(`/contact/${card.contactId}`)}>{card.contactName}</Text>
        <Muted>{[card.number, card.stageName, card.stormName, tries].filter(Boolean).join(' · ')}</Muted>
        {card.address && (
          <Text style={[styles.body, { marginTop: space.sm, color: colors.navy }]} onPress={() => openMaps(card.address!)}>{card.address}</Text>
        )}
        {card.lockedBy && <Message text={`${card.lockedBy} is on this lead right now.`} />}
      </Card>
      <Card><ContactMethods methods={card.methods} /></Card>

      <SectionTitle>How did it go?</SectionTitle>
      {!outcomes.data ? (outcomes.error ? <Message text={outcomes.error} /> : <Loading />) : (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.sm }}>
          {outcomes.data.map((d) => {
            const on = outcome === d.id;
            return (
              <Pressable key={d.id} onPress={() => setOutcome(d.id)} accessibilityRole="radio" accessibilityState={{ selected: on }}
                style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, paddingVertical: 9, paddingHorizontal: space.md, borderRadius: radius,
                  borderWidth: 1, borderColor: on ? colors.navy : colors.border, backgroundColor: on ? colors.navy : colors.white }}>
                <View style={{ width: 9, height: 9, borderRadius: 5, backgroundColor: d.color }} />
                <Text style={{ color: on ? colors.white : colors.text }}>{d.name}</Text>
              </Pressable>
            );
          })}
        </View>
      )}
      <Field label="Note (optional)" value={note} onChangeText={setNote} multiline maxLength={500} placeholder="Call back after 5" />
      <Text style={styles.label}>Call back</Text>
      <Chips value={days} onChange={setDays} options={CALL_BACK} />
      <Message text={saveError ?? error} />
      <Button title="Save and next" busy={saving || busy} disabled={!card.canEdit} onPress={save} />
      <View style={{ flexDirection: 'row', gap: space.sm }}>
        <View style={{ flex: 1 }}><Button title="Skip" kind="secondary" busy={busy} onPress={onSkip} /></View>
        <View style={{ flex: 1 }}><Button title="Open lead" kind="secondary" onPress={() => router.push(`/opportunity/${card.id}`)} /></View>
        <View style={{ flex: 1 }}><Button title="Stop" kind="secondary" onPress={onStop} /></View>
      </View>

      {card.calls.length > 0 && (
        <>
          <SectionTitle>Earlier calls</SectionTitle>
          <Card>
            {card.calls.map((c, i) => (
              <Row key={`${c.at}-${i}`} last={i === card.calls.length - 1}>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs }}>
                  <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: c.color }} />
                  <Text style={styles.body}>{c.disposition}</Text>
                </View>
                <Muted>{[when(c.at), c.by].filter(Boolean).join(' · ')}</Muted>
                {c.note && <Text style={styles.body}>{c.note}</Text>}
              </Row>
            ))}
          </Card>
        </>
      )}
    </Screen>
  );
}
