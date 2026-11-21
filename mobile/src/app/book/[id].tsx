import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Loading, Message, Muted, Screen, SectionTitle } from '@/components/ui';
import { Opportunity, Options } from '@/lib/api';
import { day, time, todayIso } from '@/lib/format';
import { outcomeMessage, useOutbox } from '@/lib/outbox';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const TIMES = Array.from({ length: 24 }, (_, i) => {
  const h = 7 + Math.floor(i / 2);
  return `${String(h).padStart(2, '0')}:${i % 2 ? '30' : '00'}`;
});
const LENGTHS = [{ label: '30 min', value: 30 }, { label: '1 hour', value: 60 }, { label: '1½ hours', value: 90 }, { label: '2 hours', value: 120 }];

/** Book an appointment on a lead: pick a day, time, length and type. Goes through the outbox, so it works offline. */
export default function Book() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const lead = useCached<Opportunity>(`/api/v1/opportunities/${id}`, "Couldn't load this lead.");
  const options = useCached<Options>('/api/v1/options', "Couldn't load the appointment types.");
  const outbox = useOutbox();
  const days = Array.from({ length: 21 }, (_, i) => todayIso(i));
  const [date, setDate] = useState(days[1]);
  const [start, setStart] = useState('10:00');
  const [length, setLength] = useState(60);
  const [type, setType] = useState<string>('inspection');
  const [title, setTitle] = useState<string | null>(null);
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!lead.data) return <Screen>{lead.error ? <Message text={lead.error} /> : <Loading />}</Screen>;
  const opp = lead.data;
  const types = options.data?.appointmentTypes ?? [];
  const typeLabel = types.find((t) => t.value === type)?.label ?? 'Appointment';
  const shownTitle = title ?? `${typeLabel} – ${opp.contactName}`;

  async function save() {
    if (!shownTitle.trim()) return setError('Give the appointment a title.');
    setBusy(true);
    setError(null);
    const outcome = await outbox.enqueue({
      kind: 'appointment', method: 'POST', path: '/api/v1/appointments',
      json: { title: shownTitle.trim(), typeKey: type, date, startTime: `${start}:00`, durationMinutes: length, notes: notes.trim() || null,
        entityType: 'opportunity', entityId: id },
      label: `Appointment: ${shownTitle.trim()} (${day(date)} ${time(`${date}T${start}`)})`,
    });
    setBusy(false);
    const m = outcomeMessage(outcome, 'Booked.');
    if (m.kind === 'danger') return setError(m.text);
    router.back();
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Book appointment' }} />
      <Card>
        <Text style={{ fontSize: 17, fontWeight: '600', color: colors.text }}>{opp.contactName}</Text>
        <Muted>{opp.address ?? opp.number}</Muted>
      </Card>

      <SectionTitle>Day</SectionTitle>
      <Chips scroll value={date} onChange={setDate}
        options={days.map((d, i) => ({ label: i === 0 ? 'Today' : i === 1 ? 'Tomorrow' : day(d), value: d }))} />

      <SectionTitle>Start</SectionTitle>
      <Chips value={start} onChange={setStart} options={TIMES.map((t) => ({ label: time(`2000-01-01T${t}`), value: t }))} />

      <SectionTitle>Length</SectionTitle>
      <Chips value={length} onChange={setLength} options={LENGTHS} />

      {types.length > 0 && (
        <>
          <SectionTitle>Type</SectionTitle>
          <Chips value={type} onChange={setType} options={types.map((t) => ({ label: t.label, value: t.value }))} />
        </>
      )}

      <Card>
        <Field label="Title" value={shownTitle} onChangeText={setTitle} />
        <Field label="Notes (optional)" value={notes} onChangeText={setNotes} multiline style={{ minHeight: 70, textAlignVertical: 'top' }} />
        <Muted>At the lead’s address. You’ll get it on your calendar.</Muted>
      </Card>
      <Message text={error} />
      <View style={{ gap: space.sm }}>
        <Button title={`Book ${day(date)}, ${time(`2000-01-01T${start}`)}`} onPress={save} busy={busy} />
        <Button title="Cancel" kind="secondary" onPress={() => router.back()} />
      </View>
    </Screen>
  );
}
