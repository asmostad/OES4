import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { ContactMethods, openMaps, WORK_ORDER_COLORS } from '@/components/Records';
import { useRecordFeed } from '@/components/RecordFeed';
import { Button, Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ApiError, WorkOrderDetail } from '@/lib/api';
import { openDocument } from '@/lib/files';
import { cents, day, label, money, when } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/**
 * A subcontractor's work order on a trade: who, when, what they're paid for and the instructions, whether they
 * accepted, and its PDF. On site, mark it done and add notes and photos. Writing and issuing it stays on the web.
 */
export default function WorkOrderScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  const r = useCached<WorkOrderDetail>(`/api/v1/work-orders/${id}`, "Couldn't load this work order.");
  const w = r.data;
  const feed = useRecordFeed('work_order', id, w?.number ?? 'a work order', 'From the site');
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [pulling, setPulling] = useState(false);
  const { reload } = r;
  const { reload: reloadFeed } = feed;
  const load = useCallback(async () => { await Promise.all([reload(), reloadFeed()]); }, [reload, reloadFeed]);

  if (!w) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;

  async function complete() {
    setBusy('complete');
    setError(null);
    try {
      await api.json(`/api/v1/work-orders/${id}/complete`, {
        method: 'POST', body: '{}', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': `${id}-complete` },
      });
      setNotice('Marked done. The office pays it from here.');
      setConfirming(false);
      await load();
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t go through. Try again.');
    } finally {
      setBusy(null);
    }
  }

  async function openPdf() {
    if (!w?.pdfDocumentId) return;
    setBusy('pdf');
    setError(null);
    try {
      await openDocument(api, { id: w.pdfDocumentId, fileName: `${w.number}.pdf`, contentType: 'application/pdf', sizeBytes: 0, category: null, createdAt: w.issuedAt ?? '' });
    } catch (x) {
      setError(x instanceof Error ? x.message : "Couldn't open the PDF.");
    } finally {
      setBusy(null);
    }
  }

  const accepted = w.acceptStatus === 'accepted'
    ? `Accepted${w.acceptedName ? ` by ${w.acceptedName}` : ''}${w.acceptedAt ? ` · ${when(w.acceptedAt)}` : ''}`
    : w.acceptStatus === 'declined' ? `Declined${w.declineReason ? `: ${w.declineReason}` : ''}` : w.status === 'issued' ? 'Not accepted yet' : null;

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await load(); setPulling(false); }}>
      <Stack.Screen options={{ title: w.number }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 20 }]}>{w.sub.name}</Text>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: space.xs }}>
          <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: WORK_ORDER_COLORS[w.status] ?? colors.slate }} />
          <Text style={{ color: colors.slate }}>{[label(w.status), money(w.amount)].join(' · ')}</Text>
        </View>
        {accepted && <Text style={{ color: w.acceptStatus === 'declined' ? colors.danger : w.acceptStatus === 'accepted' ? colors.success : colors.warning, marginTop: space.xs }}>{accepted}</Text>}
        <Text style={[styles.body, { color: colors.navy, marginTop: space.sm }]} onPress={() => router.push(`/trade/${w.tradeId}`)} accessibilityRole="link">
          {w.projectNumber} · {w.tradeName}
        </Text>
        {w.address && <Text style={styles.body}>{w.address}</Text>}
        {w.address && <View style={{ marginTop: space.md }}><Button title="Directions" kind="secondary" onPress={() => openMaps(w.address!)} /></View>}
      </Card>

      <Message text={error} />
      <Message text={notice} kind="success" />

      <SectionTitle>The work</SectionTitle>
      <Card style={{ gap: space.sm }}>
        <Text style={styles.body}>{`Work date: ${w.workDate ? day(w.workDate) : 'not set'}${w.arrivalTime ? ` · arrive ${w.arrivalTime}` : ''}`}</Text>
        {w.instructions && (
          <View>
            <Text style={styles.label}>Instructions</Text>
            <Text style={styles.body}>{w.instructions}</Text>
          </View>
        )}
        {w.completedAt && <Muted>{`Done ${when(w.completedAt)}${w.completionNote ? ` · ${w.completionNote}` : ''}`}</Muted>}
        {w.pdfDocumentId && <Button title={busy === 'pdf' ? 'Opening…' : 'Open the work order PDF'} kind="secondary" onPress={openPdf} disabled={busy === 'pdf'} />}
      </Card>

      {w.lines.length > 0 && (
        <Card>
          {w.lines.map((l, i) => (
            <Row key={l.id} last={i === w.lines.length - 1}>
              <View style={{ flexDirection: 'row', gap: space.md }}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.body}>{l.description}</Text>
                  <Muted>{`${Number(l.qty.toFixed(2))} ${l.unit} × ${cents(l.rate)}`}</Muted>
                </View>
                <Text style={styles.body}>{cents(l.amount)}</Text>
              </View>
            </Row>
          ))}
          <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginTop: space.sm }}>
            <Text style={[styles.body, { fontWeight: '700' }]}>Total</Text>
            <Text style={[styles.body, { fontWeight: '700' }]}>{cents(w.amount)}</Text>
          </View>
        </Card>
      )}

      {w.canEdit && w.status === 'issued' && (confirming ? (
        <Card style={{ gap: space.sm }}>
          <Muted>{`Mark ${w.sub.name}'s work on ${w.tradeName} as done? Add photos of the finished work below first if you have them.`}</Muted>
          <View style={{ flexDirection: 'row', gap: space.sm }}>
            <View style={{ flex: 1 }}><Button title="Cancel" kind="secondary" onPress={() => setConfirming(false)} /></View>
            <View style={{ flex: 1 }}><Button title="Mark done" onPress={complete} busy={busy === 'complete'} /></View>
          </View>
        </Card>
      ) : <Button title="Work is done" onPress={() => setConfirming(true)} />)}

      <SectionTitle>Subcontractor</SectionTitle>
      <Card><ContactMethods methods={w.sub.methods} /></Card>

      {feed.view}
    </Screen>
  );
}
