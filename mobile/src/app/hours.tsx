import { router, Stack } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Field, Loading, Message, Muted, Screen, styles } from '@/components/ui';
import { ApiError } from '@/lib/api';
import { clock, parseTime } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

type Window = { day: number; start: string; end: string };
type Draft = { start: string; end: string };
// Monday first, as on the web.
const DAYS = [1, 2, 3, 4, 5, 6, 0];
const NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/**
 * Your weekly hours for online booking: up to two windows a day (say, mornings and afternoons around lunch), in the
 * company's time zone. A day with nothing is off. Saving replaces the whole week.
 */
export default function Hours() {
  const { api } = useApi();
  const r = useCached<Window[]>('/api/v1/me/hours', "Couldn't load your hours.");
  const [draft, setDraft] = useState<Record<number, Draft[]> | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!r.data) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;
  const saved: Record<number, Draft[]> = {};
  for (const w of r.data) (saved[w.day] ??= []).push({ start: clock(w.start), end: clock(w.end) });
  const week = draft ?? saved;
  const edit = (day: number, windows: Draft[]) => setDraft({ ...week, [day]: windows });

  async function save() {
    const hours: Window[] = [];
    for (const day of DAYS) {
      for (const w of week[day] ?? []) {
        if (!w.start.trim() && !w.end.trim()) continue;
        const start = parseTime(w.start);
        const end = parseTime(w.end);
        if (!start || !end) { setError(`${NAMES[day]}: enter times like 8:00 am and 5:30 pm.`); return; }
        hours.push({ day, start, end });
      }
    }
    setBusy(true);
    setError(null);
    try {
      await api.put('/api/v1/me/hours', { hours });
      await r.reload();
      setDraft(null);
      router.back();
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t save.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Booking hours' }} />
      <Muted>When customers can book you online. Times are in the company’s time zone.</Muted>
      {DAYS.map((day) => {
        const windows = week[day] ?? [];
        return (
          <Card key={day} style={{ gap: space.sm }}>
            <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
              <Text style={[styles.body, { fontWeight: '600' }]}>{NAMES[day]}</Text>
              {windows.length === 0 ? (
                <Text style={{ color: colors.navy }} onPress={() => edit(day, [{ start: '8:00 am', end: '5:00 pm' }])} accessibilityRole="button">Set hours</Text>
              ) : <Muted>{windows.length === 1 ? '' : 'two windows'}</Muted>}
            </View>
            {windows.length === 0 && <Muted>Off</Muted>}
            {windows.map((w, i) => (
              <View key={i} style={{ flexDirection: 'row', gap: space.sm, alignItems: 'flex-end' }}>
                <View style={{ flex: 1 }}>
                  <Field label="From" value={w.start} onChangeText={(v) => edit(day, windows.map((x, j) => (j === i ? { ...x, start: v } : x)))} autoCapitalize="none" />
                </View>
                <View style={{ flex: 1 }}>
                  <Field label="To" value={w.end} onChangeText={(v) => edit(day, windows.map((x, j) => (j === i ? { ...x, end: v } : x)))} autoCapitalize="none" />
                </View>
                <Text style={{ color: colors.danger, paddingBottom: space.md }} onPress={() => edit(day, windows.filter((_, j) => j !== i))}
                  accessibilityRole="button" accessibilityLabel={`Remove ${NAMES[day]} hours`}>Remove</Text>
              </View>
            ))}
            {windows.length === 1 && (
              <Text style={{ color: colors.navy }} onPress={() => edit(day, [...windows, { start: '1:00 pm', end: '5:00 pm' }])} accessibilityRole="button">
                Add a second window
              </Text>
            )}
          </Card>
        );
      })}
      <Message text={error} />
      <Button title="Save hours" onPress={save} busy={busy} disabled={!draft} />
    </Screen>
  );
}
