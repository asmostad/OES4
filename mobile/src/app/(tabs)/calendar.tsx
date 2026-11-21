import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { AppointmentRow } from '@/lib/api';
import { day, label, time, todayIso } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, radius, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/** Monday of the week containing the phone's date, plus whole weeks. */
function weekStart(offsetWeeks: number): string {
  const d = new Date();
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7) + offsetWeeks * 7);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
const addDays = (iso: string, n: number) => {
  const [y, m, d] = iso.split('-').map(Number);
  const x = new Date(y, m - 1, d + n);
  return `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`;
};

/** My own appointments (even for managers who can see everyone's), a week at a time (Monday to Sunday), grouped by day. Saved for offline. */
export default function Calendar() {
  const { me } = useApi();
  const [week, setWeek] = useState(0);
  const [pulling, setPulling] = useState(false);
  const from = weekStart(week);
  const to = addDays(from, 6);
  const list = useCached<AppointmentRow[]>(`/api/v1/appointments?from=${from}&to=${to}&user=${me.userId}`, "Couldn't load the calendar.");
  const today = todayIso();

  // Each day of the week, with its appointments (multi-day all-day events show on each day they cover).
  const days = Array.from({ length: 7 }, (_, i) => addDays(from, i)).map((date) => ({
    date,
    items: (list.data ?? []).filter((a) => a.startLocal.slice(0, 10) <= date && (a.isAllDay ? a.endLocal.slice(0, 10) > date || a.startLocal.slice(0, 10) === date : a.startLocal.slice(0, 10) === date))
      .sort((a, b) => Number(b.isAllDay) - Number(a.isAllDay) || a.startLocal.localeCompare(b.startLocal)),
  }));

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await list.reload(); setPulling(false); }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
        <Pressable onPress={() => setWeek(week - 1)} hitSlop={10} accessibilityRole="button" accessibilityLabel="Previous week">
          <Ionicons name="chevron-back" size={26} color={colors.navy} />
        </Pressable>
        <Pressable onPress={() => setWeek(0)} accessibilityRole="button" accessibilityLabel="This week">
          <Text style={[styles.title, { textAlign: 'center' }]}>{day(from)} – {day(to)}</Text>
          <Muted>{week === 0 ? 'This week' : week === 1 ? 'Next week' : week === -1 ? 'Last week' : 'Tap for this week'}</Muted>
        </Pressable>
        <Pressable onPress={() => setWeek(week + 1)} hitSlop={10} accessibilityRole="button" accessibilityLabel="Next week">
          <Ionicons name="chevron-forward" size={26} color={colors.navy} />
        </Pressable>
      </View>
      <OfflineNote offline={list.offline} savedAt={list.savedAt} />
      <Message text={list.error} />
      {!list.data && !list.error && <Loading />}
      {list.data && days.map((d) => (
        <View key={d.date} style={{ gap: space.sm }}>
          <SectionTitle>{day(d.date)}{d.date === today ? ' · Today' : ''}</SectionTitle>
          {d.items.length === 0 ? <Muted>Nothing scheduled.</Muted> : (
            <Card style={d.date === today ? { borderLeftWidth: 3, borderLeftColor: colors.orange } : undefined}>
              {d.items.map((a, i) => {
                return (
                  <Row key={`${a.id}-${d.date}`} last={i === d.items.length - 1} onPress={() => router.push({ pathname: '/appointment/[id]', params: { id: a.id, date: a.startLocal.slice(0, 10) } })}>
                    <View style={{ flexDirection: 'row', gap: space.md }}>
                      <Text style={{ width: 72, fontWeight: '600', color: colors.navy }}>{a.isAllDay ? 'All day' : time(a.startLocal)}</Text>
                      <View style={{ flex: 1 }}>
                        <Text style={[styles.body, a.status === 'cancelled' && { textDecorationLine: 'line-through', color: colors.muted }]}>{a.title}</Text>
                        <Muted>{[label(a.typeKey), a.isAllDay ? null : `until ${time(a.endLocal)}`, a.location].filter(Boolean).join(' · ')}</Muted>
                      </View>
                      <Ionicons name="chevron-forward" size={18} color={colors.muted} />
                    </View>
                  </Row>
                );
              })}
            </Card>
          )}
        </View>
      ))}
      <View style={{ height: space.sm, borderRadius: radius }} />
    </Screen>
  );
}
