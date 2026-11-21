import Ionicons from '@expo/vector-icons/Ionicons';
import { Text, View } from 'react-native';

import { when } from '@/lib/format';
import { colors, radius, space } from '@/lib/theme';

/** "Offline · showing what was saved Nov 20, 9:42 AM", while a screen shows its saved copy. */
export function OfflineNote({ offline, savedAt }: { offline: boolean; savedAt: string | null }) {
  if (!offline) return null;
  return (
    <View accessibilityRole="alert" style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm, padding: space.md, borderRadius: radius, backgroundColor: '#EEF2F4' }}>
      <Ionicons name="cloud-offline-outline" size={18} color={colors.slate} />
      <Text style={{ flex: 1, color: colors.slate }}>Offline · showing what was saved{savedAt ? ` ${when(savedAt)}` : ''}</Text>
    </View>
  );
}
