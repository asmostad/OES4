import Ionicons from '@expo/vector-icons/Ionicons';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Linking, Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { LeadList, openMaps } from '@/components/Records';
import { Button, Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { PropertyDetail, PropertyMeasurements } from '@/lib/api';
import { day, label } from '@/lib/format';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/** A property: directions, who's on it, and its leads. Saved for offline. */
export default function PropertyScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const p = useCached<PropertyDetail>(`/api/v1/properties/${id}`, "Couldn't load this property.");
  const measurements = useCached<PropertyMeasurements>(`/api/v1/properties/${id}/measurements`, "Couldn't load the measurements.");
  const mm = measurements.data;
  const [pulling, setPulling] = useState(false);
  if (!p.data) return <Screen>{p.error ? <Message text={p.error} /> : <Loading />}</Screen>;
  const d = p.data;
  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await Promise.all([p.reload(), measurements.reload()]); setPulling(false); }}>
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

      {mm && (mm.current.length > 0 || mm.canMeasure) && mm.trades.length > 0 && (
        <>
          <SectionTitle>Measurements</SectionTitle>
          <Card>
            {mm.current.length === 0 && <Muted>Nothing measured yet.</Muted>}
            {mm.current.map((s, i) => (
              <Row key={s.id} last={i === mm.current.length - 1} onPress={() => router.push(`/measurements/${s.id}`)}>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
                  <View style={{ width: 10, height: 10, borderRadius: 3, backgroundColor: s.tradeColor }} />
                  <View style={{ flex: 1 }}>
                    <Text style={styles.body}>{`${s.tradeName} · ${s.structureName}`}</Text>
                    <Muted>{[s.headline, `v${s.versionNo}`, s.sourceLabel, s.measuredOn ? day(s.measuredOn) : null].filter(Boolean).join(' · ')}</Muted>
                  </View>
                </View>
              </Row>
            ))}
          </Card>
          {mm.canMeasure && <Button title="Measure" kind="secondary" onPress={() => router.push({ pathname: '/measure', params: { propertyId: id } })} />}
        </>
      )}
    </Screen>
  );
}
