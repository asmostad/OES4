import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Loading, Message, Screen, styles } from '@/components/ui';
import { ApiError, Opportunity, Options } from '@/lib/api';
import { useApi } from '@/lib/session';
import { space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/** A lead's details: its name, expected value, how it came in, whether it's insurance, and what it's about. */
export default function EditLead() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  const r = useCached<Opportunity>(`/api/v1/opportunities/${id}`, "Couldn't load this lead.");
  const options = useCached<Options>('/api/v1/options', "Couldn't load lead sources.");
  const [form, setForm] = useState<{ name: string; value: string; source: string | null; insurance: boolean; description: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  if (!r.data) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;
  const o = r.data;
  const f = form ?? {
    name: o.name, value: o.expectedValue != null ? String(o.expectedValue) : '', source: o.sourceKey ?? null, insurance: !!o.isInsurance, description: o.description ?? '',
  };
  const set = (patch: Partial<typeof f>) => setForm({ ...f, ...patch });

  async function save() {
    const value = f.value.trim() ? Number(f.value.replace(/[$,\s]/g, '')) : null;
    if (value != null && (Number.isNaN(value) || value < 0)) { setError('Enter the expected value as a number, like 18500.'); return; }
    setBusy(true);
    setError(null);
    try {
      await api.put(`/api/v1/opportunities/${id}`, {
        name: f.name.trim() || null, expectedValue: value, isInsurance: f.insurance, sourceKey: f.source, description: f.description.trim() || null,
      });
      router.back();
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t save.');
    } finally {
      setBusy(false);
    }
  }

  const sources = options.data?.leadSources ?? [];
  return (
    <Screen>
      <Stack.Screen options={{ title: `Edit ${o.number}` }} />
      <Card>
        <Field label="Name" value={f.name} onChangeText={(v) => set({ name: v })} />
        <Field label="Expected value" value={f.value} onChangeText={(v) => set({ value: v })} keyboardType="decimal-pad" placeholder="18500" />
        <Field label="What it's about" value={f.description} onChangeText={(v) => set({ description: v })} multiline style={{ minHeight: 80, textAlignVertical: 'top' }} />
      </Card>
      {sources.length > 0 && (
        <View style={{ gap: space.sm }}>
          <Text style={styles.label}>How it came in</Text>
          <Chips scroll value={f.source} onChange={(v) => set({ source: v })} options={sources.map((s) => ({ label: s.label, value: s.value as string | null }))} />
        </View>
      )}
      <Chips value={f.insurance} onChange={(v) => set({ insurance: v })} options={[{ label: 'Retail', value: false }, { label: 'Insurance', value: true }]} />
      <Message text={error} />
      <Button title="Save" onPress={save} busy={busy} disabled={!form} />
    </Screen>
  );
}
