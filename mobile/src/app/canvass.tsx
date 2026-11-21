import * as Location from 'expo-location';
import { router, Stack } from 'expo-router';
import { useEffect, useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { openMaps } from '@/components/Records';
import { Button, Card, Chips, Loading, Message, Muted, Row, Screen, styles } from '@/components/ui';
import { CanvassDoor, Storm } from '@/lib/api';
import { day, label } from '@/lib/format';
import { space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

type Spot = { lat: number; lng: number };
/** Rounded to about 1 km, so the saved list is reused as you walk a street. */
const round = (lat: number, lng: number): Spot => ({ lat: Math.round(lat * 100) / 100, lng: Math.round(lng * 100) / 100 });

/**
 * Door knocking after a storm: pick the storm, and the houses in its ZIP codes with no lead on it yet come up, nearest
 * first when you allow location. Tap a house for directions or to take a lead at the door, tied to the storm.
 * Saved for offline, so the list is still there in a dead zone.
 */
export default function Canvass() {
  const storms = useCached<Storm[]>('/api/v1/storms', "Couldn't load the storms.");
  const [stormId, setStormId] = useState<string | null>(null);
  const storm = storms.data?.find((s) => s.id === stormId) ?? storms.data?.[0];
  const [spot, setSpot] = useState<Spot | null>(null);
  const [locating, setLocating] = useState(false);
  const [locationNote, setLocationNote] = useState<string | null>(null);
  const [pulling, setPulling] = useState(false);
  const [open, setOpen] = useState<string | null>(null);

  async function locate() {
    try {
      const current = await Location.getForegroundPermissionsAsync();
      const permission = current.granted ? current : await Location.requestForegroundPermissionsAsync();
      if (!permission.granted) {
        setLocationNote('Allow location in Settings to sort by the nearest door.');
        return;
      }
      const p = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
      setSpot(round(p.coords.latitude, p.coords.longitude));
      setLocationNote(null);
    } catch {
      setLocationNote("Couldn't find where you are. Showing by street.");
    } finally {
      setLocating(false);
    }
  }

  // Use the location if it's already allowed; asking waits for the button.
  useEffect(() => {
    let live = true;
    Location.getForegroundPermissionsAsync()
      .then((p) => (p.granted ? Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced }) : null))
      .then((p) => { if (live && p) setSpot(round(p.coords.latitude, p.coords.longitude)); })
      .catch(() => { /* by street */ });
    return () => { live = false; };
  }, []);

  const path = storm ? `/api/v1/storms/${storm.id}/canvass${spot ? `?lat=${spot.lat}&lng=${spot.lng}` : ''}` : null;
  // Until the storms load there is no list to fetch; the storms path is only a stand-in.
  const doors = useCached<CanvassDoor[]>(path ?? '/api/v1/storms', "Couldn't load the canvass list.");

  if (!storms.data) return <Screen>{storms.error ? <Message text={storms.error} /> : <Loading />}</Screen>;
  if (storms.data.length === 0) return <Screen><Message text="No storms yet. The office adds them on the web with their ZIP codes." /></Screen>;

  const list = path ? doors.data ?? [] : [];
  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await Promise.all([storms.reload(), doors.reload()]); setPulling(false); }}>
      <Stack.Screen options={{ title: 'Canvass' }} />
      <OfflineNote offline={doors.offline} savedAt={doors.savedAt} />
      <Chips scroll value={storm?.id ?? null} onChange={setStormId}
        options={storms.data.map((s) => ({ label: `${s.name} · ${day(s.eventDate)}`, value: s.id as string | null }))} />
      {storm && (
        <Muted>{[label(storm.perilKey), storm.maxHailSizeIn ? `${storm.maxHailSizeIn}" hail` : null, storm.maxWindMph ? `${storm.maxWindMph} mph wind` : null,
          `${storm.zipCount} ZIP${storm.zipCount === 1 ? '' : 's'}`, `${storm.opportunities} lead${storm.opportunities === 1 ? '' : 's'} so far`].filter(Boolean).join(' · ')}</Muted>
      )}
      {!spot && <Button title={locating ? 'Finding you…' : 'Sort by nearest door'} kind="secondary" onPress={() => { setLocating(true); locate(); }} disabled={locating} />}
      <Message text={locationNote} />

      {storm && storm.zipCount === 0 && <Message text="This storm has no ZIP codes yet, so there's no list. Add them on the web." />}
      {path && !doors.data && !doors.error && <Loading />}
      <Message text={doors.error} />
      {doors.data && list.length === 0 && storm?.zipCount !== 0 && <Muted>Every house on file in this storm already has a lead.</Muted>}
      {list.length > 0 && (
        <Card>
          {list.map((d, i) => (
            <Row key={d.propertyId} last={i === list.length - 1} onPress={() => setOpen(open === d.propertyId ? null : d.propertyId)}>
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.body}>{[d.line1, d.line2].filter(Boolean).join(', ')}</Text>
                  <Muted>{d.cityLine}</Muted>
                </View>
                {d.miles != null && <Muted>{d.miles < 0.1 ? 'here' : `${d.miles.toFixed(1)} mi`}</Muted>}
              </View>
              {open === d.propertyId && (
                <View style={{ flexDirection: 'row', gap: space.sm, marginTop: space.sm }}>
                  <View style={{ flex: 1 }}>
                    <Button title="New lead" onPress={() => router.push({
                      pathname: '/new-lead',
                      params: { propertyId: d.propertyId, address: `${d.line1}, ${d.cityLine}`, stormEventId: storm!.id, stormName: storm!.name },
                    })} />
                  </View>
                  <View style={{ flex: 1 }}><Button title="Directions" kind="secondary" onPress={() => openMaps(`${d.line1}, ${d.cityLine}`)} /></View>
                  <View style={{ flex: 1 }}><Button title="Property" kind="secondary" onPress={() => router.push(`/property/${d.propertyId}`)} /></View>
                </View>
              )}
            </Row>
          ))}
        </Card>
      )}
    </Screen>
  );
}
