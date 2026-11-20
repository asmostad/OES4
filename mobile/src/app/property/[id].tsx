import Ionicons from '@expo/vector-icons/Ionicons';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Linking, Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { LeadList, openMaps } from '@/components/Records';
import { Button, Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { PropertyDetail } from '@/lib/api';
import { label } from '@/lib/format';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/** A property: directions, who's on it, and its leads. Saved for offline. */
export default function PropertyScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const p = useCached<PropertyDetail>(`/api/v1/properties/${id}`, "Couldn't load this property.");
  const [pulling, setPulling] = useState(false);
  if (!p.data) return <Screen>{p.error ? <Message text={p.error} /> : <Loading />}</Screen>;
  const d = p.data;
  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await p.reload(); setPulling(false); }}>
      <Stack.Screen options={{ title: 'Property' }} />
      <OfflineNote offline={p.offline} savedAt={p.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 19 }]}>{d.address}</Text>
        <View style={{ marginTop: space.md }}><Button title="Directions" kind="secondary" onPress={() => openMaps(d.address)} /></View>
      </Card>
      <SectionTitle>People</SectionTitle>
      <Card>
        {d.contacts.length === 0 && <Muted>No one linked.</Muted>}
        {d.contacts.map((c, i) => (
          <Row key={c.id} last={i === d.contacts.length - 1} onPress={() => router.push(`/contact/${c.id}`)}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
              <View style={{ flex: 1 }}>
                <Text style={styles.body}>{c.name}</Text>
                <Muted>{[label(c.role), c.isPrimary ? 'primary' : null, c.phone].filter(Boolean).join(' · ')}</Muted>
              </View>
              {c.phone && <Ionicons name="call" size={22} color={colors.navy} onPress={() => Linking.openURL(`tel:${c.phone!.replace(/[^\d+]/g, '')}`)} accessibilityLabel={`Call ${c.name}`} />}
            </View>
          </Row>
        ))}
      </Card>
      <SectionTitle>Leads</SectionTitle>
      <LeadList leads={d.opportunities} />
    </Screen>
  );
}
