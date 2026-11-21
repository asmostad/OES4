import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Button, Card, Field, Loading, Message, Muted, Row, Screen, styles } from '@/components/ui';
import { ApiError, ChangeOrderDetail } from '@/lib/api';
import { openFile } from '@/lib/files';
import { cents, label, when } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const newKey = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
const STATUS_COLORS: Record<string, string> = { draft: '#64748b', approved: '#16a34a', rejected: '#dc2626' };

/** A change order on a job: what and why, the amount (or its items), the PDF, and the customer's yes or no on site. */
export default function ChangeOrderScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  const r = useCached<ChangeOrderDetail>(`/api/v1/change-orders/${id}`, "Couldn't load this change order.");
  const [deciding, setDeciding] = useState<'approve' | 'reject' | null>(null);
  const [name, setName] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const c = r.data;
  if (!c) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;

  async function decide() {
    if (!deciding) return;
    setBusy('decide');
    setError(null);
    try {
      await api.json(`/api/v1/change-orders/${id}/decide`, {
        method: 'POST', body: JSON.stringify({ approve: deciding === 'approve', approvedByName: deciding === 'approve' ? name.trim() : null }),
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': newKey() },
      });
      setNotice(deciding === 'approve' ? 'Approved. It’s added to the contract.' : 'Marked rejected.');
      setDeciding(null);
      await r.reload();
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t go through.');
    } finally {
      setBusy(null);
    }
  }

  async function pdf() {
    setBusy('pdf');
    setError(null);
    try {
      await openFile(api, `/api/v1/change-orders/${id}/document?handler=Pdf`, `${c!.projectNumber}-change-order-${c!.sequence}.pdf`);
    } catch (x) {
      setError(x instanceof Error ? x.message : "Couldn't open the PDF.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: `Change order #${c.sequence}` }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 19 }]}>{c.description}</Text>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: space.xs }}>
          <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: STATUS_COLORS[c.status] ?? colors.slate }} />
          <Text style={{ color: colors.slate }}>{[label(c.status), c.isInsuranceSupplement ? 'insurance supplement' : null].filter(Boolean).join(' · ')}</Text>
        </View>
        <Text style={[styles.title, { fontSize: 24, marginTop: space.sm }]}>{cents(c.amount)}</Text>
        {c.reason && <Text style={[styles.body, { marginTop: space.xs }]}>{c.reason}</Text>}
        <Text style={[styles.body, { color: colors.navy, marginTop: space.sm }]} onPress={() => router.push(`/project/${c.projectId}`)} accessibilityRole="link">
          {[c.projectNumber, c.customerName, c.tradeName].filter(Boolean).join(' · ')}
        </Text>
        {c.address && <Muted>{c.address}</Muted>}
        {c.approvedByName && <Muted>{`Approved by ${c.approvedByName}${c.decidedAt ? ` · ${when(c.decidedAt)}` : ''}`}</Muted>}
        {c.status === 'rejected' && c.decidedAt && <Muted>{`Rejected ${when(c.decidedAt)}`}</Muted>}
        <View style={{ marginTop: space.md }}>
          <Button title={busy === 'pdf' ? 'Opening…' : 'Open the PDF'} kind="secondary" onPress={pdf} disabled={busy === 'pdf'} />
        </View>
      </Card>

      <Message text={error} />
      <Message text={notice} kind="success" />

      {c.lines.length > 0 && (
        <Card>
          {c.lines.map((l, i) => (
            <Row key={l.id} last={i === c.lines.length - 1}>
              <View style={{ flexDirection: 'row', gap: space.md }}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.body}>{l.description}</Text>
                  <Muted>{[l.code, `${Number(l.qty.toFixed(2))} ${l.unit} × ${cents(l.unitPrice)}`].filter(Boolean).join(' · ')}</Muted>
                </View>
                <Text style={styles.body}>{cents(l.amount)}</Text>
              </View>
            </Row>
          ))}
        </Card>
      )}

      {c.canDecide && !deciding && (
        <View style={{ gap: space.sm }}>
          <Button title="Customer approved" onPress={() => { setDeciding('approve'); setName(c.customerName); setNotice(null); }} />
          <Button title="Customer said no" kind="secondary" onPress={() => { setDeciding('reject'); setNotice(null); }} />
        </View>
      )}
      {deciding && (
        <Card style={{ gap: space.sm }}>
          {deciding === 'approve' ? (
            <>
              <Field label="Who approved it" value={name} onChangeText={setName} autoCapitalize="words" />
              <Muted>{`Adds ${cents(c.amount)} to the contract.`}</Muted>
            </>
          ) : <Muted>Marks this change order rejected. Nothing changes on the contract.</Muted>}
          <View style={{ flexDirection: 'row', gap: space.sm }}>
            <View style={{ flex: 1 }}><Button title="Cancel" kind="secondary" onPress={() => setDeciding(null)} /></View>
            <View style={{ flex: 1 }}>
              <Button title={deciding === 'approve' ? 'Approve' : 'Reject'} onPress={decide} busy={busy === 'decide'} disabled={deciding === 'approve' && !name.trim()} />
            </View>
          </View>
        </Card>
      )}
      {c.status === 'draft' && c.isItemized && c.lines.length === 0 && <Muted>Add this change order’s items on the web before it can be approved.</Muted>}
    </Screen>
  );
}
