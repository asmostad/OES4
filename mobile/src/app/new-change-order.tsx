import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Message, Muted, Screen, styles } from '@/components/ui';
import { ApiError, ProjectDetail } from '@/lib/api';
import { useApi } from '@/lib/session';
import { space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const newKey = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

/**
 * Extra work (or a credit) agreed on site, for one amount. It starts as a draft; the customer approves it on the next
 * screen. Itemizing from the price list stays on the web. Needs a signal, so the number is on the job right away.
 */
export default function NewChangeOrder() {
  const { projectId } = useLocalSearchParams<{ projectId: string }>();
  const { api } = useApi();
  const project = useCached<ProjectDetail>(`/api/v1/projects/${projectId}`, "Couldn't load the project.");
  const [tradeId, setTradeId] = useState<string | null>(null);
  const [description, setDescription] = useState('');
  const [amount, setAmount] = useState('');
  const [credit, setCredit] = useState(false);
  const [reason, setReason] = useState('');
  const [supplement, setSupplement] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    const n = Number(amount.replace(/[$,\s]/g, ''));
    if (!description.trim()) return setError('Describe the change.');
    if (!amount.trim() || Number.isNaN(n) || n <= 0) return setError('Enter the amount, like 540.');
    setBusy(true);
    setError(null);
    try {
      const { id } = await api.json<{ id: string }>(`/api/v1/projects/${projectId}/change-orders`, {
        method: 'POST',
        body: JSON.stringify({ tradeId, description: description.trim(), amount: credit ? -n : n, reason: reason.trim() || null, isInsuranceSupplement: supplement }),
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': newKey() },
      });
      router.replace(`/change-order/${id}`);
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t save.');
    } finally {
      setBusy(false);
    }
  }

  const trades = project.data?.trades ?? [];
  return (
    <Screen>
      <Stack.Screen options={{ title: 'Change order' }} />
      {project.data && <Muted>{`${project.data.number} · ${project.data.contactName}`}</Muted>}
      {trades.length > 0 && (
        <View style={{ gap: space.sm }}>
          <Text style={styles.label}>Trade</Text>
          <Chips scroll value={tradeId} onChange={setTradeId} options={[{ label: 'Whole job', value: null as string | null }, ...trades.map((t) => ({ label: t.name, value: t.id as string | null }))]} />
        </View>
      )}
      <Card>
        <Field label="What's changing" value={description} onChangeText={setDescription} placeholder="Replace 6 sheets of rotten decking" />
        <Chips value={credit} onChange={setCredit} options={[{ label: 'Extra charge', value: false }, { label: 'Credit', value: true }]} />
        <Field label="Amount" value={amount} onChangeText={setAmount} keyboardType="decimal-pad" placeholder="540" />
        <Field label="Why (optional)" value={reason} onChangeText={setReason} placeholder="Found at tear-off" />
      </Card>
      <Chips value={supplement} onChange={setSupplement} options={[{ label: 'Customer pays', value: false }, { label: 'Insurance supplement', value: true }]} />
      <Message text={error} />
      <Button title="Save change order" onPress={save} busy={busy} />
    </Screen>
  );
}
