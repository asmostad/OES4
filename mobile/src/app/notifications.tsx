import Ionicons from '@expo/vector-icons/Ionicons';
import { router, Stack } from 'expo-router';
import { useEffect, useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Card, Loading, Message, Muted, Row, Screen, styles } from '@/components/ui';
import { Bell, NotificationItem } from '@/lib/api';
import { when } from '@/lib/format';
import { setBadge } from '@/lib/push';
import { routeFor } from '@/lib/routes';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

type IconName = React.ComponentProps<typeof Ionicons>['name'];
function iconFor(kind: string): IconName {
  if (kind.startsWith('task')) return 'checkbox-outline';
  if (kind.startsWith('appointment') || kind.startsWith('booking')) return 'calendar-outline';
  if (kind.startsWith('message') || kind.startsWith('sms') || kind.startsWith('email')) return 'chatbubble-outline';
  if (kind.startsWith('opportunity') || kind.startsWith('lead')) return 'briefcase-outline';
  return 'notifications-outline';
}

/** The bell: what needs the user's attention. Tapping one marks it read and opens it when the app has that screen. */
export default function Notifications() {
  const { api } = useApi();
  const bell = useCached<Bell>('/api/v1/notifications', "Couldn't load notifications.");
  const [read, setRead] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [pulling, setPulling] = useState(false);

  const items = bell.data?.items.map((n) => (read.has(n.id) ? { ...n, read: true } : n)) ?? [];
  const unread = items.filter((n) => !n.read).length;
  useEffect(() => { if (bell.data && !bell.offline) setBadge(unread); }, [bell.data, bell.offline, unread]);

  async function open(n: NotificationItem) {
    if (!n.read) {
      setRead((s) => new Set(s).add(n.id));
      api.put(`/api/v1/notifications/${n.id}/read`, {}).catch(() => {}); // best effort: it stays unread on the server if offline
    }
    const to = routeFor(n.url);
    if (to) router.push(to);
  }

  async function readAll() {
    setError(null);
    try {
      await api.post('/api/v1/notifications/read-all', {});
      setRead(new Set(items.map((n) => n.id)));
    } catch {
      setError("Couldn't mark them read. Try again when you have a signal.");
    }
  }

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); setRead(new Set()); await bell.reload(); setPulling(false); }}>
      <Stack.Screen options={{
        title: 'Notifications',
        headerRight: unread > 0 ? () => <Text style={{ color: colors.white, fontWeight: '600' }} onPress={readAll} accessibilityRole="button">Mark all read</Text> : undefined,
      }} />
      {!bell.data ? (bell.error ? <Message text={bell.error} /> : <Loading />) : (
        <>
          <OfflineNote offline={bell.offline} savedAt={bell.savedAt} />
          <Message text={error} />
          <Card>
            {items.length === 0 && <Muted>Nothing yet. Assigned tasks, bookings and replies show up here.</Muted>}
            {items.map((n, i) => (
              <Row key={n.id} last={i === items.length - 1} onPress={() => open(n)}>
                <View style={{ flexDirection: 'row', gap: space.md, alignItems: 'flex-start' }}>
                  <Ionicons name={iconFor(n.kind)} size={20} color={n.read ? colors.muted : colors.navy} style={{ marginTop: 2 }} />
                  <View style={{ flex: 1 }}>
                    <Text style={[styles.body, !n.read && { fontWeight: '700' }]}>{n.title}</Text>
                    {n.body && <Muted>{n.body}</Muted>}
                    <Muted>{when(n.createdAt)}</Muted>
                  </View>
                  {!n.read && <View style={{ width: 9, height: 9, borderRadius: 5, backgroundColor: colors.navy, marginTop: 6 }} accessibilityLabel="Unread" />}
                </View>
              </Row>
            ))}
          </Card>
        </>
      )}
    </Screen>
  );
}
