import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Message, Muted, Screen, SectionTitle, styles } from '@/components/ui';
import { Options } from '@/lib/api';
import { outcomeMessage, useOutbox } from '@/lib/outbox';
import { space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/**
 * A lead taken in the field: name, phone, email, the address, how it came in (door knock by default), and whether
 * it's an insurance job. One step on the server; through the outbox, so it works with no signal.
 */
export default function NewLead() {
  // From a storm's canvass list: the house is already on file, and the lead is tied to the storm.
  const door = useLocalSearchParams<{ propertyId?: string; address?: string; stormEventId?: string; stormName?: string }>();
  const outbox = useOutbox();
  const options = useCached<Options>('/api/v1/options', "Couldn't load lead sources.");
  const [f, setF] = useState({ firstName: '', lastName: '', phone: '', email: '', line1: '', city: '', state: '', postalCode: '', notes: '' });
  const [source, setSource] = useState('door_knock');
  const [insurance, setInsurance] = useState(!!door.stormEventId);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (key: keyof typeof f) => (value: string) => setF({ ...f, [key]: value });

  async function save() {
    if (!f.firstName.trim() && !f.lastName.trim()) return setError('Enter their name.');
    if (!door.propertyId && f.line1.trim() && (!f.city.trim() || !f.state.trim() || !f.postalCode.trim())) return setError('Finish the address: city, state and ZIP.');
    setBusy(true);
    setError(null);
    const name = `${f.firstName} ${f.lastName}`.trim();
    const street = door.address ?? f.line1.trim();
    const outcome = await outbox.enqueue({
      kind: 'lead', method: 'POST', path: '/api/v1/leads',
      json: {
        ...Object.fromEntries(Object.entries(f).map(([k, v]) => [k, v.trim() || null])), sourceKey: source, isInsurance: insurance,
        propertyId: door.propertyId ?? null, stormEventId: door.stormEventId ?? null,
      },
      label: `New lead: ${name}${street ? `, ${street}` : ''}`,
    });
    setBusy(false);
    if (outcome.status === 'failed') return setError(outcomeMessage(outcome, '').text);
    const id = outcome.status === 'sent' ? (outcome.body as { opportunityId?: string } | undefined)?.opportunityId : undefined;
    if (id) router.replace(`/opportunity/${id}`);
    else router.back(); // queued: it's on the waiting list and becomes a lead when the connection's back
  }

  const sources = options.data?.leadSources ?? [{ value: 'door_knock', label: 'Door Knock' }];

  return (
    <Screen>
      <Stack.Screen options={{ title: 'New lead' }} />
      <Card>
        <View style={{ flexDirection: 'row', gap: space.sm }}>
          <View style={{ flex: 1 }}><Field label="First name" value={f.firstName} onChangeText={set('firstName')} autoCapitalize="words" textContentType="givenName" /></View>
          <View style={{ flex: 1 }}><Field label="Last name" value={f.lastName} onChangeText={set('lastName')} autoCapitalize="words" textContentType="familyName" /></View>
        </View>
        <Field label="Mobile" value={f.phone} onChangeText={set('phone')} keyboardType="phone-pad" textContentType="telephoneNumber" />
        <Field label="Email" value={f.email} onChangeText={set('email')} keyboardType="email-address" autoCapitalize="none" autoCorrect={false} />
      </Card>

      <SectionTitle>Address</SectionTitle>
      {door.propertyId ? (
        <Card>
          <Text style={styles.body}>{door.address}</Text>
          {door.stormName && <Muted>{`Canvassing ${door.stormName}`}</Muted>}
        </Card>
      ) : (
      <Card>
        <Field label="Street" value={f.line1} onChangeText={set('line1')} textContentType="fullStreetAddress" placeholder="123 Main St" />
        <Field label="City" value={f.city} onChangeText={set('city')} textContentType="addressCity" />
        <View style={{ flexDirection: 'row', gap: space.sm }}>
          <View style={{ flex: 1 }}><Field label="State" value={f.state} onChangeText={set('state')} autoCapitalize="characters" maxLength={2} textContentType="addressState" /></View>
          <View style={{ flex: 2 }}><Field label="ZIP" value={f.postalCode} onChangeText={set('postalCode')} keyboardType="number-pad" maxLength={10} textContentType="postalCode" /></View>
        </View>
      </Card>
      )}

      <SectionTitle>How it came in</SectionTitle>
      <Chips value={source} onChange={setSource} options={sources.map((s) => ({ label: s.label, value: s.value }))} />
      <SectionTitle>Insurance claim?</SectionTitle>
      <Chips value={insurance} onChange={setInsurance} options={[{ label: 'No', value: false }, { label: 'Yes, insurance', value: true }]} />

      <Card>
        <Field label="Notes (optional)" value={f.notes} onChangeText={set('notes')} multiline placeholder="What they need, what you saw"
          style={{ minHeight: 80, textAlignVertical: 'top' }} />
      </Card>
      <Message text={error} />
      <View style={{ gap: space.sm }}>
        <Button title="Save lead" onPress={save} busy={busy} />
        <Button title="Cancel" kind="secondary" onPress={() => router.back()} />
      </View>
    </Screen>
  );
}
