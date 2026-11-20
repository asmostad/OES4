import Constants from 'expo-constants';
import { Text } from 'react-native';

import { Button, Card, Muted, Screen, styles } from '@/components/ui';
import { useApi, useSession } from '@/lib/session';

export default function Account() {
  const { api, me } = useApi();
  const { signOut } = useSession();
  return (
    <Screen>
      <Card>
        <Text style={styles.title}>{me.firstName} {me.lastName}</Text>
        <Muted>{me.email}</Muted>
        <Muted>{me.tenantName}</Muted>
      </Card>
      <Card>
        <Muted>Server: {api.server}</Muted>
        <Muted>App version {Constants.expoConfig?.version ?? ''}</Muted>
      </Card>
      <Button title="Sign out" kind="secondary" onPress={signOut} />
    </Screen>
  );
}
