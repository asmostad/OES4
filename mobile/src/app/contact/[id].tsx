import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { ContactMethods, LeadList } from '@/components/Records';
import { Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ContactDetail } from '@/lib/api';
import { label } from '@/lib/format';
import { useCached } from '@/lib/useCached';

/** A contact: how to reach them, their properties and their leads. Saved for offline. */
export default function ContactScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const c = useCached<ContactDetail>(`/api/v1/contacts/${id}`, "Couldn't load this contact.");
  const [pulling, setPulling] = useState(false);
  if (!c.data) return <Screen>{c.error ? <Message text={c.error} /> : <Loading />}</Screen>;
  const d = c.data;
  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await c.reload(); setPulling(false); }}>
      <Stack.Screen options={{ title: d.name }} />
      <OfflineNote offline={c.offline} savedAt={c.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 20 }]}>{d.name}</Text>
        <Muted>{d.type === 'organization' ? 'Organization' : 'Person'}{d.ownerName ? ` · owner ${d.ownerName}` : ''}</Muted>
      </Card>
      <SectionTitle>Reach them</SectionTitle>
      <Card><ContactMethods methods={d.methods} /></Card>
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
