import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { Pressable, Text, View } from 'react-native';

import { useOutbox } from '@/lib/outbox';
import { colors, radius, space } from '@/lib/theme';

/** "3 waiting to send · Send now" and "1 couldn't be sent · Review", shown only when something's in the outbox. */
export function OutboxBanner() {
  const { waiting, failed, sending, flush } = useOutbox();
  if (waiting === 0 && failed === 0) return null;
  return (
    <View style={{ gap: space.sm }} accessibilityLiveRegion="polite">
      {waiting > 0 && (
        <Pressable onPress={flush} disabled={sending} accessibilityRole="button"
          style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm, padding: space.md, borderRadius: radius, backgroundColor: '#FFF4E8', borderWidth: 1, borderColor: '#FFD2A8' }}>
          <Ionicons name="cloud-upload-outline" size={20} color={colors.warning} />
          <Text style={{ flex: 1, color: colors.warning }}>{waiting} waiting to send{sending ? ' · sending…' : ''}</Text>
          {!sending && <Text style={{ color: colors.navy, fontWeight: '600' }}>Send now</Text>}
        </Pressable>
      )}
      {failed > 0 && (
        <Pressable onPress={() => router.push('/account')} accessibilityRole="button"
          style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm, padding: space.md, borderRadius: radius, backgroundColor: colors.dangerBg }}>
          <Ionicons name="alert-circle-outline" size={20} color={colors.danger} />
          <Text style={{ flex: 1, color: colors.danger }}>{failed} couldn’t be sent</Text>
          <Text style={{ color: colors.navy, fontWeight: '600' }}>Review</Text>
        </Pressable>
      )}
    </View>
  );
}
