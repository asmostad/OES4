import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert, Platform, Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { openMaps } from '@/components/Records';
import { Button, Card, Chips, Field, Loading, Message, Muted, Screen, styles } from '@/components/ui';
import { ApiError, AppointmentRow } from '@/lib/api';
import { routeFor } from '@/lib/routes';
import { day, label, time, todayIso } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const TIMES = Array.from({ length: 28 }, (_, i) => {
  const h = 6 + Math.floor(i / 2);
  return `${String(h).padStart(2, '0')}:${i % 2 ? '30' : '00'}`;
});
const LENGTHS = [{ label: '30 min', value: 30 }, { label: '1 hour', value: 60 }, { label: '1½ hours', value: 90 }, { label: '2 hours', value: 120 }, { label: '3 hours', value: 180 }];
const minutes = (local: string) => { const [h, m] = local.slice(11, 16).split(':').map(Number); return h * 60 + m; };

/**
 * One appointment: when and where, the record it's for, and on the day: directions, mark it done, move it, or cancel
 * it (everyone on it is told). Saved for offline reading; changes need a signal.
 */
export default function AppointmentScreen() {
  const { id, date } = useLocalSearchParams<{ id: string; date: string }>();
  const { api, me } = useApi();
  const r = useCached<AppointmentRow[]>(`/api/v1/appointments?from=${date}&to=${date}&user=${me.userId}`, "Couldn't load the appointment.");
  const a = r.data?.find((x) => x.id === id);
  const [moving, setMoving] = useState(false);
  const [newDate, setNewDate] = useState(date);
  const [start, setStart] = useState<string | null>(null);
  const [length, setLength] = useState<number | null>(null);
  const [location, setLocation] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  if (!r.data) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;
  if (!a) return <Screen><Message text="This appointment isn't on your calendar for that day any more." /></Screen>;

  const record = a.recordUrl ? routeFor(a.recordUrl) : null;
  const startNow = start ?? a.startLocal.slice(11, 16);
  const lengthNow = length ?? Math.max(30, minutes(a.endLocal) - minutes(a.startLocal));
  const days = [...new Set([date, ...Array.from({ length: 21 }, (_, i) => todayIso(i))])].sort();

  async function run(what: string, fn: () => Promise<unknown>, done: string) {
    setBusy(what);
    setError(null);
    try {
      await fn();
      setNotice(done);
      setMoving(false);
      await r.reload();
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t go through.');
    } finally {
      setBusy(null);
    }
  }

  const status = (s: string, done: string) => run(s, () => api.put(`/api/v1/appointments/${id}/status`, { status: s }), done);

  function cancel() {
    const go = () => status('cancelled', 'Cancelled. Everyone on it was told.');
    if (Platform.OS === 'web') { if (window.confirm(`Cancel "${a!.title}"?`)) go(); return; }
    Alert.alert('Cancel this appointment?', `"${a!.title}" comes off the calendar and everyone on it is told.`,
      [{ text: 'Keep it', style: 'cancel' }, { text: 'Cancel it', style: 'destructive', onPress: go }]);
  }

  async function move() {
    await run('move', async () => {
      await api.put(`/api/v1/appointments/${id}`, {
        title: a!.title, typeKey: a!.typeKey, date: newDate, startTime: startNow, durationMinutes: lengthNow,
        location: (location ?? a!.location ?? '').trim() || null, notes: a!.notes, allDay: false,
      });
      // It may have moved to another day: show it there.
      if (newDate !== date) router.setParams({ date: newDate });
    }, 'Moved. Whoever’s on it was told.');
  }

  const cancelled = a.status === 'cancelled';
  return (
    <Screen>
      <Stack.Screen options={{ title: label(a.typeKey) || 'Appointment' }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 19 }, cancelled && { textDecorationLine: 'line-through', color: colors.muted }]}>{a.title}</Text>
        <Text style={[styles.body, { marginTop: space.xs }]}>
          {a.isAllDay ? `${day(a.startLocal)} · all day` : `${day(a.startLocal)} · ${time(a.startLocal)} – ${time(a.endLocal)}`}
        </Text>
        <Muted>{[label(a.status), a.assigneeName, a.attendeeNames?.length ? `with ${a.attendeeNames.join(', ')}` : null].filter(Boolean).join(' · ')}</Muted>
        {a.location && <Text style={[styles.body, { marginTop: space.sm }]}>{a.location}</Text>}
        {a.notes && <Text style={[styles.body, { marginTop: space.sm }]}>{a.notes}</Text>}
        <View style={{ flexDirection: 'row', gap: space.sm, marginTop: space.md }}>
          {a.location && <View style={{ flex: 1 }}><Button title="Directions" kind="secondary" onPress={() => openMaps(a.location!)} /></View>}
          {record && <View style={{ flex: 1 }}><Button title="Open the record" kind="secondary" onPress={() => router.push(record)} /></View>}
        </View>
      </Card>

      <Message text={error} />
      <Message text={notice} kind="success" />

      {a.canEdit && !cancelled && !moving && (
        <View style={{ gap: space.sm }}>
          {a.status !== 'completed' && <Button title="Mark done" onPress={() => status('completed', 'Marked done.')} busy={busy === 'completed'} />}
          {a.status === 'completed' && <Button title="Not done yet" kind="secondary" onPress={() => status('scheduled', 'Back to scheduled.')} busy={busy === 'scheduled'} />}
          {!a.isAllDay && <Button title="Move it" kind="secondary" onPress={() => { setMoving(true); setNotice(null); }} />}
          <Button title="Cancel it" kind="secondary" onPress={cancel} busy={busy === 'cancelled'} />
        </View>
      )}

      {moving && (
        <Card style={{ gap: space.md }}>
          <View style={{ gap: space.sm }}>
            <Text style={styles.label}>Day</Text>
            <Chips scroll value={newDate} onChange={setNewDate} options={days.map((d) => ({ label: d === todayIso() ? 'Today' : day(d), value: d }))} />
          </View>
          <View style={{ gap: space.sm }}>
            <Text style={styles.label}>Start</Text>
            <Chips scroll value={startNow} onChange={setStart} options={TIMES.map((t) => ({ label: time(`2000-01-01T${t}`), value: t }))} />
          </View>
          <View style={{ gap: space.sm }}>
            <Text style={styles.label}>How long</Text>
            <Chips scroll value={lengthNow} onChange={setLength} options={LENGTHS} />
          </View>
          <Field label="Where" value={location ?? a.location ?? ''} onChangeText={setLocation} />
          <View style={{ flexDirection: 'row', gap: space.sm }}>
            <View style={{ flex: 1 }}><Button title="Cancel" kind="secondary" onPress={() => setMoving(false)} /></View>
            <View style={{ flex: 1 }}><Button title="Save" onPress={move} busy={busy === 'move'} /></View>
          </View>
        </Card>
      )}
      {cancelled && <Muted>This appointment was cancelled.</Muted>}
    </Screen>
  );
}
