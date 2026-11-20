import Ionicons from '@expo/vector-icons/Ionicons';
import { router, usePathname } from 'expo-router';
import { useEffect, useState } from 'react';
import { Text, View } from 'react-native';

import { Bell } from '@/lib/api';
import { useApi } from '@/lib/session';
import { colors } from '@/lib/theme';

/** Search and the bell (with the unread count), on every tab. The count refreshes each minute and when the user changes screen. */
export function HeaderButtons() {
  const { api } = useApi();
  const path = usePathname();
  const [unread, setUnread] = useState(0);
  useEffect(() => {
    let live = true;
    const load = () => api.get<Bell>('/api/v1/notifications?take=1').then((b) => { if (live) setUnread(b.unread); }).catch(() => {});
    load();
    const t = setInterval(load, 60_000);
    return () => { live = false; clearInterval(t); };
  }, [api, path]);

  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 18, marginRight: 16 }}>
      <Ionicons name="search" size={22} color={colors.white} onPress={() => router.push('/search')} accessibilityLabel="Search" />
      <View>
        <Ionicons name={unread > 0 ? 'notifications' : 'notifications-outline'} size={22} color={colors.white}
          onPress={() => router.push('/notifications')} accessibilityLabel={unread > 0 ? `Notifications, ${unread} unread` : 'Notifications'} />
        {unread > 0 && (
          <View pointerEvents="none" style={{ position: 'absolute', top: -5, right: -8, minWidth: 17, height: 17, borderRadius: 9, paddingHorizontal: 4,
            backgroundColor: colors.danger, alignItems: 'center', justifyContent: 'center' }}>
            <Text style={{ color: colors.white, fontSize: 11, fontWeight: '700' }}>{unread > 99 ? '99+' : unread}</Text>
          </View>
        )}
      </View>
    </View>
  );
}
