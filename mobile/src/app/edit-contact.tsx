import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Loading, Message, Muted, Screen } from '@/components/ui';
import { ApiError, ContactDetail, ContactMethod } from '@/lib/api';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const KINDS = [{ label: 'Mobile', value: 'mobile' }, { label: 'Phone', value: 'phone' }, { label: 'Email', value: 'email' }];

/** Fix a contact's name and how to reach them (a wrong number, a new email). Owner, notes and the rest stay as they are. */
export default function EditContact() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  const r = useCached<ContactDetail>(`/api/v1/contacts/${id}`, "Couldn't load this contact.");
  const [form, setForm] = useState<{ first: string; last: string; org: string; methods: ContactMethod[] } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  if (!r.data) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;
  const d = r.data;
  const org = d.type === 'organization';
  const f = form ?? { first: d.firstName ?? '', last: d.lastName ?? '', org: d.organizationName ?? '', methods: d.methods.filter((m) => KINDS.some((k) => k.value === m.kind)) };
  const set = (patch: Partial<typeof f>) => setForm({ ...f, ...patch });
  const setMethod = (i: number, patch: Partial<ContactMethod>) => set({ methods: f.methods.map((m, j) => (j === i ? { ...m, ...patch } : m)) });
  // Numbers the app doesn't edit (fax, website) are kept as they are.
  const others = d.methods.filter((m) => !KINDS.some((k) => k.value === m.kind));

  function primary(i: number) {
    const kind = f.methods[i].kind;
    set({ methods: f.methods.map((m, j) => (m.kind === kind ? { ...m, isPrimary: j === i } : m)) });
  }

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await api.put(`/api/v1/contacts/${id}`, {
        firstName: org ? null : f.first.trim(), lastName: org ? null : f.last.trim(), organizationName: org ? f.org.trim() : null,
        methods: [...f.methods.filter((m) => m.value.trim()), ...others],
      });
      router.back();
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t save.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Edit contact' }} />
      <Card>
        {org ? <Field label="Organization name" value={f.org} onChangeText={(v) => set({ org: v })} /> : (
          <View style={{ flexDirection: 'row', gap: space.sm }}>
            <View style={{ flex: 1 }}><Field label="First name" value={f.first} onChangeText={(v) => set({ first: v })} autoCapitalize="words" /></View>
            <View style={{ flex: 1 }}><Field label="Last name" value={f.last} onChangeText={(v) => set({ last: v })} autoCapitalize="words" /></View>
          </View>
        )}
      </Card>
      {f.methods.map((m, i) => (
        <Card key={i} style={{ gap: space.sm }}>
          <Chips value={m.kind} onChange={(k) => setMethod(i, { kind: k })} options={KINDS} />
          <Field label={m.kind === 'email' ? 'Email' : 'Number'} value={m.value} onChangeText={(v) => setMethod(i, { value: v })}
            keyboardType={m.kind === 'email' ? 'email-address' : 'phone-pad'} autoCapitalize="none" autoCorrect={false} />
          <View style={{ flexDirection: 'row', justifyContent: 'space-between' }}>
            {m.isPrimary ? <Muted>Primary</Muted> : <Text style={{ color: colors.navy }} onPress={() => primary(i)} accessibilityRole="button">Make primary</Text>}
            <Text style={{ color: colors.danger }} onPress={() => set({ methods: f.methods.filter((_, j) => j !== i) })} accessibilityRole="button">Remove</Text>
          </View>
        </Card>
      ))}
      <View style={{ flexDirection: 'row', gap: space.sm }}>
        <View style={{ flex: 1 }}><Button title="Add a number" kind="secondary" onPress={() => set({ methods: [...f.methods, { kind: 'mobile', value: '', isPrimary: !f.methods.some((x) => x.kind !== 'email') }] })} /></View>
        <View style={{ flex: 1 }}><Button title="Add an email" kind="secondary" onPress={() => set({ methods: [...f.methods, { kind: 'email', value: '', isPrimary: !f.methods.some((x) => x.kind === 'email') }] })} /></View>
      </View>
      {others.length > 0 && <Muted>{`Also on file and kept: ${others.map((m) => m.value).join(', ')}`}</Muted>}
      <Message text={error} />
      <Button title="Save" onPress={save} busy={busy} disabled={!form} />
    </Screen>
  );
}
