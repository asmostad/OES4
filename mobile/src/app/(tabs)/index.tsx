import { router } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { OutboxBanner } from '@/components/OutboxBanner';
import { TaskItem } from '@/components/TaskRow';
import { Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { MyDay } from '@/lib/api';
import { label, time } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/** Today: appointments, then overdue and due-today tasks (the API's my-day). Saved for offline. */
export default function Today() {
  const { me } = useApi();
  const day = useCached<MyDay>('/api/v1/my-day', "Couldn't load your day.");
  const [pulling, setPulling] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const data = day.data;

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await day.reload(); setPulling(false); }}>
      <Text style={{ fontSize: 22, fontWeight: '700', color: colors.text }}>Hi {me.firstName}</Text>
      <OfflineNote offline={day.offline} savedAt={day.savedAt} />
      <Message text={day.error ?? actionError} />
      <OutboxBanner />
      {!data && !day.error && <Loading />}
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
              <TaskItem key={t.id} task={t} last={i === data.tasks.length - 1} onChanged={day.reload} onError={setActionError} />
            ))}
          </Card>
        </>
      )}
    </Screen>
  );
}
