import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { ContactMethods, LeadList } from '@/components/Records';
import { Button, Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ContactDetail } from '@/lib/api';
import { label } from '@/lib/format';
import { useApi } from '@/lib/session';
import { useCached } from '@/lib/useCached';

/** A contact: how to reach them, their properties and their leads. Saved for offline. */
export default function ContactScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const c = useCached<ContactDetail>(`/api/v1/contacts/${id}`, "Couldn't load this contact.");
  const [pulling, setPulling] = useState(false);
  const { me } = useApi();
  if (!c.data) return <Screen>{c.error ? <Message text={c.error} /> : <Loading />}</Screen>;
  const d = c.data;
  const send = (channel: 'sms' | 'email') =>
    router.push({ pathname: '/compose', params: { entityType: 'contact', entityId: d.id, channel, label: d.name } });
  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await c.reload(); setPulling(false); }}>
      <Stack.Screen options={{ title: d.name }} />
      <OfflineNote offline={c.offline} savedAt={c.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 20 }]}>{d.name}</Text>
        <Muted>{d.type === 'organization' ? 'Organization' : 'Person'}{d.ownerName ? ` · owner ${d.ownerName}` : ''}</Muted>
        {d.canEdit && <View style={{ marginTop: 12 }}><Button title="Edit name, phone or email" kind="secondary" onPress={() => router.push({ pathname: '/edit-contact', params: { id: d.id } })} /></View>}
      </Card>
      <SectionTitle>Reach them</SectionTitle>
      <Card>
        <ContactMethods methods={d.methods} />
        <View style={{ flexDirection: 'row', gap: 8, marginTop: 12 }}>
          {me.permissions.includes('sms.send') && <View style={{ flex: 1 }}><Button title="Send a text" kind="secondary" onPress={() => send('sms')} /></View>}
          {me.permissions.includes('email.send') && <View style={{ flex: 1 }}><Button title="Send an email" kind="secondary" onPress={() => send('email')} /></View>}
        </View>
      </Card>
      <SectionTitle>Properties</SectionTitle>
      <Card>
        {d.properties.length === 0 && <Muted>No properties.</Muted>}
        {d.properties.map((p, i) => (
          <Row key={p.id} last={i === d.properties.length - 1} onPress={() => router.push(`/property/${p.id}`)}>
            <Text style={styles.body}>{p.label}</Text>
            {p.role && <Muted>{label(p.role)}</Muted>}
          </Row>
        ))}
      </Card>
      <SectionTitle>Leads</SectionTitle>
      <LeadList leads={d.opportunities} />
    </Screen>
  );
}
