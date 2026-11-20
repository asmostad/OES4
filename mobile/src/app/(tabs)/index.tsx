import { router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Text, View } from 'react-native';

import { OutboxBanner } from '@/components/OutboxBanner';
import { TaskItem } from '@/components/TaskRow';
import { Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ApiError, MyDay } from '@/lib/api';
import { label, time } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';

/** Today: appointments, then overdue and due-today tasks (the API's my-day). */
export default function Today() {
  const { api, me } = useApi();
  const [data, setData] = useState<MyDay | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setData(await api.get<MyDay>('/api/v1/my-day'));
      setError(null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't load your day.");
    }
  }, [api]);

  useFocusEffect(useCallback(() => { load(); }, [load]));

  async function refresh() {
    setRefreshing(true);
    await load();
    setRefreshing(false);
  }

  return (
    <Screen refreshing={refreshing} onRefresh={refresh}>
      <Text style={{ fontSize: 22, fontWeight: '700', color: colors.text }}>Hi {me.firstName}</Text>
      <Message text={error} />
      <OutboxBanner />
      {!data && !error && <Loading />}
      {data && (
        <>
          <View style={{ flexDirection: 'row', gap: space.md }}>
            <Card style={{ flex: 1 }}>
              <Text style={{ fontSize: 28, fontWeight: '700', color: data.overdue > 0 ? colors.danger : colors.text }}>{data.overdue}</Text>
              <Muted>Overdue</Muted>
            </Card>
            <Card style={{ flex: 1 }}>
              <Text style={{ fontSize: 28, fontWeight: '700', color: colors.text }}>{data.dueToday}</Text>
              <Muted>Due today</Muted>
            </Card>
          </View>

          <SectionTitle>Appointments</SectionTitle>
          <Card>
            {data.appointments.length === 0 && <Muted>Nothing on the calendar today.</Muted>}
            {data.appointments.map((a, i) => {
              const lead = a.entityType === 'opportunity' && a.entityId;
              return (
                <Row key={a.id} last={i === data.appointments.length - 1} onPress={lead ? () => router.push(`/opportunity/${a.entityId}`) : undefined}>
                  <Text style={{ fontWeight: '600', color: colors.navy }}>{a.isAllDay ? 'All day' : `${time(a.startLocal)} – ${time(a.endLocal)}`}</Text>
                  <Text style={styles.body}>{a.title}</Text>
                  <Muted>{[label(a.typeKey), a.location].filter(Boolean).join(' · ')}</Muted>
                </Row>
              );
            })}
          </Card>

          <SectionTitle>Tasks</SectionTitle>
          <Card>
            {data.tasks.length === 0 && <Muted>You’re all caught up.</Muted>}
            {data.tasks.map((t, i) => (
              <TaskItem key={t.id} task={t} last={i === data.tasks.length - 1} onChanged={load} onError={setError} />
            ))}
          </Card>
        </>
      )}
    </Screen>
  );
}
