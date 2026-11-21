import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { ESTIMATE_COLORS } from '@/components/Records';
import { Button, Card, Field, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ApiError, EstimateDetail, EstimateVersion } from '@/lib/api';
import { openDocument } from '@/lib/files';
import { cents, label, money, when } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const pct = (n: number) => `${Number(n.toFixed(2))}%`;
const newKey = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

/**
 * An estimate on the phone: what's in it, its versions and their proposal PDFs. At the kitchen table you can issue
 * it (when nothing is waiting for approval), open the proposal, and record that the customer signed or declined.
 * Building and pricing it stays on the web. Saved for offline reading; the actions need a signal.
 */
export default function EstimateScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  const r = useCached<EstimateDetail>(`/api/v1/estimates/${id}`, "Couldn't load this estimate.");
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [step, setStep] = useState<'issue' | 'sign' | 'decline' | null>(null);
  const [text, setText] = useState('');
  const [refreshing, setRefreshing] = useState(false);
  const { reload } = r;

  const post = useCallback(async <T,>(path: string, body: unknown) =>
    api.json<T>(path, { method: 'POST', body: JSON.stringify(body), headers: { 'Content-Type': 'application/json', 'Idempotency-Key': newKey() } }), [api]);

  const e = r.data;
  if (!e) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;

  const latest = e.versions[0];
  const signable = e.canEdit && e.status === 'issued' && latest?.status === 'issued';
  const issuable = e.canEdit && e.status === 'draft' && e.pendingApprovals === 0 && e.rejectedLines === 0 && e.sections.some((s) => s.lines.length > 0);

  function fail(x: unknown, fallback: string) {
    setError(x instanceof ApiError || x instanceof Error ? x.message : fallback);
    setNotice(null);
  }

  async function openPdf(v: EstimateVersion) {
    setBusy(`pdf-${v.id}`);
    setError(null);
    try {
      // The saved copy, or the server makes and saves one first (the signed copy after signing).
      const { documentId } = await api.get<{ documentId: string }>(`/api/v1/estimate-versions/${v.id}/proposal?handler=Copy`);
      await openDocument(api, {
        id: documentId, fileName: `${e!.number}-v${v.versionNo}${v.status === 'signed' ? '-signed' : ''}.pdf`, contentType: 'application/pdf',
        sizeBytes: 0, category: null, createdAt: v.issuedAt,
      });
      if (!v.hasPdf) await reload();
    } catch (x) {
      fail(x, "Couldn't open the proposal.");
    } finally {
      setBusy(null);
    }
  }

  async function act() {
    if (!step) return;
    setBusy(step);
    setError(null);
    try {
      if (step === 'issue') {
        await post(`/api/v1/estimates/${id}/issue`, { revisionReason: text.trim() || null });
        setNotice('Issued. This version is now frozen; open its proposal below.');
      } else if (step === 'sign') {
        await post(`/api/v1/estimate-versions/${latest!.id}/sign`, { signerName: text.trim() });
        setNotice('Marked signed. The lead is now won.');
      } else {
        await post(`/api/v1/estimate-versions/${latest!.id}/decline`, { reason: text.trim() || null });
        setNotice('Marked declined.');
      }
      setStep(null);
      setText('');
      await reload();
    } catch (x) {
      fail(x, 'That didn’t go through. Try again.');
    } finally {
      setBusy(null);
    }
  }

  function start(s: 'issue' | 'sign' | 'decline') {
    setStep(s);
    setText(s === 'sign' ? e!.contactName : '');
    setError(null);
    setNotice(null);
  }

  return (
    <Screen refreshing={refreshing} onRefresh={async () => { setRefreshing(true); await reload(); setRefreshing(false); }}>
      <Stack.Screen options={{ title: e.number }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 20 }]}>{e.name || e.number}</Text>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: space.xs }}>
          <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: ESTIMATE_COLORS[e.status] ?? colors.slate }} />
          <Text style={{ color: colors.slate }}>{[label(e.status), latest ? `v${latest.versionNo}` : null, e.isArchived ? 'archived' : null].filter(Boolean).join(' · ')}</Text>
        </View>
        <Text style={[styles.title, { fontSize: 26, marginTop: space.sm }]}>{money(e.total)}</Text>
        <Text style={[styles.body, { color: colors.navy, marginTop: space.sm }]} onPress={() => router.push(`/opportunity/${e.opportunityId}`)} accessibilityRole="link">
          {e.contactName} · {e.opportunityName}
        </Text>
        {e.address && <Muted>{e.address}</Muted>}
        <Muted>{[e.ownerName ? `Owner: ${e.ownerName}` : null, e.claimNumber ? `Claim ${e.claimNumber}` : null, e.priceListName,
          e.expiresOn ? `Good until ${e.expiresOn}` : null].filter(Boolean).join(' · ')}</Muted>
      </Card>

      <Message text={error} />
      <Message text={notice} kind="success" />
      {e.pendingApprovals > 0 && <Message text={`${e.pendingApprovals} price exception(s) are waiting for approval before this can be issued.`} />}
      {e.rejectedLines > 0 && <Message text="Some lines were rejected. Fix them on the web before issuing." />}

      {(issuable || signable) && !step && (
        <View style={{ gap: space.sm }}>
          {issuable && <Button title={latest ? `Issue revision v${latest.versionNo + 1}` : 'Issue this estimate'} onPress={() => start('issue')} />}
          {signable && <Button title="Customer signed" onPress={() => start('sign')} />}
          {signable && <Button title="Customer declined" kind="secondary" onPress={() => start('decline')} />}
        </View>
      )}
      {step && (
        <Card style={{ gap: space.sm }}>
          {step === 'issue' && <Muted>Issuing freezes this version so it can be sent and signed. Later changes start a new revision.</Muted>}
          {step === 'issue' && latest && <Field label="What changed in this revision" value={text} onChangeText={setText} placeholder="Added gutters" />}
          {step === 'sign' && <Field label="Who signed" value={text} onChangeText={setText} autoCapitalize="words" />}
          {step === 'sign' && <Muted>{`Records that ${text.trim() || 'the customer'} signed v${latest?.versionNo} for ${money(latest?.total)}. The lead moves to won.`}</Muted>}
          {step === 'decline' && <Field label="Why (optional)" value={text} onChangeText={setText} placeholder="Went with another roofer" />}
          <View style={{ flexDirection: 'row', gap: space.sm }}>
            <View style={{ flex: 1 }}><Button title="Cancel" kind="secondary" onPress={() => { setStep(null); setText(''); }} /></View>
            <View style={{ flex: 1 }}>
              <Button title={step === 'issue' ? 'Issue' : step === 'sign' ? 'Mark signed' : 'Mark declined'} onPress={act} busy={busy === step}
                disabled={(step === 'sign' && !text.trim()) || (step === 'issue' && !!latest && !text.trim())} />
            </View>
          </View>
        </Card>
      )}

      {e.versions.length > 0 && (
        <>
          <SectionTitle>Proposals</SectionTitle>
          <Card>
            {e.versions.map((v, i) => (
              <Row key={v.id} last={i === e.versions.length - 1} onPress={() => openPdf(v)}>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.body}>{`Version ${v.versionNo} · ${label(v.status)} · ${money(v.total)}`}</Text>
                    <Muted>{v.status === 'signed' && v.signerName
                      ? `Signed by ${v.signerName}${v.signedAt ? ` · ${when(v.signedAt)}` : ''}`
                      : [`Issued ${when(v.issuedAt)}`, v.issuedBy, v.revisionReason].filter(Boolean).join(' · ')}</Muted>
                  </View>
                  <Muted>{busy === `pdf-${v.id}` ? 'Opening…' : 'PDF'}</Muted>
                </View>
              </Row>
            ))}
          </Card>
        </>
      )}

      {e.sections.map((s) => (
        <View key={s.id}>
          <SectionTitle>{s.name}</SectionTitle>
          <Card>
            {s.scopeOfWork && <Text style={[styles.body, { marginBottom: space.sm }]}>{s.scopeOfWork}</Text>}
            {s.lines.length === 0 && <Muted>No lines.</Muted>}
            {s.lines.map((l, i) => (
              <Row key={l.id} last={i === s.lines.length - 1}>
                <View style={{ flexDirection: 'row', gap: space.md }}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.body}>{l.description}</Text>
                    <Muted>{[l.code, `${Number(l.qty.toFixed(2))} ${l.unit} × ${cents(l.unitPrice)}`].filter(Boolean).join(' · ')}</Muted>
                    {l.approvalStatus === 'pending' && <Text style={{ color: colors.warning }}>Waiting for price approval</Text>}
                    {l.approvalStatus === 'rejected' && <Text style={{ color: colors.danger }}>Price rejected</Text>}
                    {l.notes && <Muted>{l.notes}</Muted>}
                  </View>
                  <Text style={styles.body}>{cents(l.amount)}</Text>
                </View>
              </Row>
            ))}
            {s.lines.length > 0 && (
              <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginTop: space.sm }}>
                <Muted>Section total</Muted>
                <Text style={[styles.body, { fontWeight: '600' }]}>{cents(s.subtotal)}</Text>
              </View>
            )}
          </Card>
        </View>
      ))}

      <SectionTitle>Totals</SectionTitle>
      <Card>
        <Total name="Subtotal" value={e.subtotal} />
        {e.overhead !== 0 && <Total name={`Overhead (${pct(e.overheadPct)})`} value={e.overhead} />}
        {e.profit !== 0 && <Total name={`Profit (${pct(e.profitPct)})`} value={e.profit} />}
        {e.tax !== 0 && <Total name={`Tax (${pct(e.taxRate)})`} value={e.tax} />}
        <Total name="Total" value={e.total} strong />
      </Card>
      {e.canEdit && e.status !== 'signed' && <Muted>To change lines or prices, open this estimate on the web.</Muted>}
    </Screen>
  );
}

function Total({ name, value, strong }: { name: string; value: number; strong?: boolean }) {
  return (
    <View style={{ flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 4 }}>
      <Text style={[styles.body, strong && { fontWeight: '700' }]}>{name}</Text>
      <Text style={[styles.body, strong && { fontWeight: '700' }]}>{cents(value)}</Text>
    </View>
  );
}
