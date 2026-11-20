import Constants from 'expo-constants';
import { Alert, Platform, Pressable, Text, View } from 'react-native';

import { Button, Card, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { when } from '@/lib/format';
import { OutboxItem, useOutbox } from '@/lib/outbox';
import { useApi, useSession } from '@/lib/session';
import { colors, space } from '@/lib/theme';

export default function Account() {
  const { api, me } = useApi();
  const { signOut } = useSession();
  const outbox = useOutbox();

  function confirmDiscard(item: OutboxItem) {
    const go = () => outbox.discard(item.key);
    if (Platform.OS === 'web') {
      if (window.confirm(`Discard "${item.label}"? It won't be sent.`)) go();
    } else {
      Alert.alert('Discard this?', `"${item.label}" won't be sent.`, [{ text: 'Keep', style: 'cancel' }, { text: 'Discard', style: 'destructive', onPress: go }]);
    }
  }

  function confirmSignOut() {
    if (outbox.items.length === 0) return signOut();
    const text = `${outbox.items.length} change${outbox.items.length === 1 ? ' is' : 's are'} still waiting to send. They stay on this phone and send after you sign in again as ${me.firstName}.`;
    if (Platform.OS === 'web') {
      if (window.confirm(text)) signOut();
    } else {
      Alert.alert('Sign out?', text, [{ text: 'Stay signed in', style: 'cancel' }, { text: 'Sign out', onPress: signOut }]);
    }
  }

  return (
    <Screen>
      <Card>
        <Text style={styles.title}>{me.firstName} {me.lastName}</Text>
        <Muted>{me.email}</Muted>
        <Muted>{me.tenantName}</Muted>
      </Card>

      <SectionTitle>Waiting to send</SectionTitle>
      <Card>
        {outbox.items.length === 0 && <Muted>Everything’s sent.</Muted>}
        {outbox.items.map((item, i) => (
          <Row key={item.key} last={i === outbox.items.length - 1}>
            <Text style={styles.body}>{item.label}</Text>
            <Muted>{item.error ? `Refused: ${item.error}` : `Waiting since ${when(item.createdAt)}`}</Muted>
            {item.error && (
              <View style={{ flexDirection: 'row', gap: space.lg, marginTop: space.xs }}>
                <Pressable onPress={() => outbox.retry(item.key)} accessibilityRole="button">
                  <Text style={{ color: colors.navy, fontWeight: '600' }}>Try again</Text>
                </Pressable>
                <Pressable onPress={() => confirmDiscard(item)} accessibilityRole="button">
                  <Text style={{ color: colors.danger, fontWeight: '600' }}>Discard</Text>
                </Pressable>
              </View>
            )}
          </Row>
        ))}
        {outbox.waiting > 0 && (
          <View style={{ marginTop: space.md }}>
            <Button title={outbox.sending ? 'Sending…' : 'Send now'} kind="secondary" onPress={outbox.flush} busy={outbox.sending} />
          </View>
        )}
      </Card>

      <Card>
        <Muted>Server: {api.server}</Muted>
        <Muted>App version {Constants.expoConfig?.version ?? ''}</Muted>
      </Card>
      <Button title="Sign out" kind="secondary" onPress={confirmSignOut} />
    </Screen>
  );
}
