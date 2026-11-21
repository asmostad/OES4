import Ionicons from '@expo/vector-icons/Ionicons';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Linking, Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { CLAIM_COLORS, ContactMethods } from '@/components/Records';
import { useRecordFeed } from '@/components/RecordFeed';
import { Button, Card, Chips, Field, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ApiError, ClaimDetail, Person } from '@/lib/api';
import { day, label, money, todayIso } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/**
 * An insurance claim on a lead: carrier, numbers, date of loss, the adjuster to call, supplements and what's approved.
 * After the adjuster meeting, update the status, claim number, inspection day, approved amount and notes. Carriers,
 * adjusters and supplements are set up on the web.
 */
export default function ClaimScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  const r = useCached<ClaimDetail>(`/api/v1/claims/${id}`, "Couldn't load this claim.");
  const c = r.data;
  const feed = useRecordFeed('claim', id, c?.claimNumber ? `claim ${c.claimNumber}` : 'the claim', 'Add to this claim');
  const [editing, setEditing] = useState(false);
  const [status, setStatus] = useState('');
  const [claimNumber, setClaimNumber] = useState('');
  const [inspection, setInspection] = useState<string | null>(null);
  const [approved, setApproved] = useState('');
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pulling, setPulling] = useState(false);
  const { reload } = r;
  const { reload: reloadFeed } = feed;
  const load = useCallback(async () => { await Promise.all([reload(), reloadFeed()]); }, [reload, reloadFeed]);

  if (!c) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;

  function edit() {
    setStatus(c!.status);
    setClaimNumber(c!.claimNumber ?? '');
    setInspection(c!.inspectionOn);
    setApproved(c!.approvedAmount != null ? String(c!.approvedAmount) : '');
    setNotes(c!.notes ?? '');
    setError(null);
    setNotice(null);
    setEditing(true);
  }

  async function save() {
    const amount = approved.trim() ? Number(approved.replace(/[$,\s]/g, '')) : null;
    if (amount != null && (Number.isNaN(amount) || amount < 0)) { setError('Enter the approved amount as a number, like 14250.50.'); return; }
    setBusy(true);
    setError(null);
    try {
      await api.put(`/api/v1/claims/${id}/field`, {
        status, claimNumber: claimNumber.trim() || null, inspectionOn: inspection, approvedAmount: amount, notes: notes.trim() || null,
      });
      setEditing(false);
      setNotice('Claim updated.');
      await load();
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t save. Try again.');
    } finally {
      setBusy(false);
    }
  }

  // The current inspection day first (it may be in the past), then the next two weeks.
  const days = [...new Set([...(inspection ? [inspection] : []), ...Array.from({ length: 14 }, (_, i) => todayIso(i))])];

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await load(); setPulling(false); }}>
      <Stack.Screen options={{ title: c.claimNumber ?? 'Claim' }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 20 }]}>{c.carrier?.name ?? 'Carrier not set'}</Text>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: space.xs }}>
          <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: CLAIM_COLORS[c.status] ?? colors.slate }} />
          <Text style={{ color: colors.slate }}>{[label(c.status), c.isArchived ? 'archived' : null].filter(Boolean).join(' · ')}</Text>
        </View>
        <Text style={[styles.body, { color: colors.navy, marginTop: space.sm }]} onPress={() => router.push(`/opportunity/${c.opportunityId}`)} accessibilityRole="link">
          {c.contactName} · {c.opportunityNumber}
        </Text>
        {c.address && <Muted>{c.address}</Muted>}
      </Card>

      <Message text={error} />
      <Message text={notice} kind="success" />

      <SectionTitle>Claim</SectionTitle>
      <Card>
        <Fact name="Claim number" value={c.claimNumber} />
        <Fact name="Policy number" value={c.policyNumber} />
        <Fact name="Date of loss" value={c.dateOfLoss ? day(c.dateOfLoss) : null} />
        <Fact name="Peril" value={c.perilKey ? label(c.perilKey) : null} />
        <Fact name="Storm" value={c.stormName} />
        <Fact name="Deductible" value={c.deductible != null ? money(c.deductible) : null} />
        <Fact name="Filed" value={c.filedOn ? day(c.filedOn) : null} />
        <Fact name="Inspection" value={c.inspectionOn ? day(c.inspectionOn) : null} />
        <Fact name="Approved" value={c.approvedAmount != null ? money(c.approvedAmount) : null} />
        {c.totalApproved != null && c.totalApproved !== c.approvedAmount && <Fact name="With supplements" value={money(c.totalApproved)} />}
        {c.notes && <Text style={[styles.body, { marginTop: space.sm }]}>{c.notes}</Text>}
      </Card>

      {c.canEdit && !editing && <Button title="Update after the inspection" kind="secondary" onPress={edit} />}
      {editing && (
        <Card style={{ gap: space.md }}>
          <View style={{ gap: space.sm }}>
            <Text style={styles.label}>Status</Text>
            <Chips value={status} onChange={(v) => setStatus(v)} options={c.statuses.map((s) => ({ label: label(s), value: s }))} />
          </View>
          <Field label="Claim number" value={claimNumber} onChangeText={setClaimNumber} autoCapitalize="characters" />
          <View style={{ gap: space.sm }}>
            <Text style={styles.label}>Inspection day</Text>
            <Chips scroll value={inspection} onChange={setInspection}
              options={[{ label: 'None', value: null as string | null }, ...days.map((d) => ({ label: d === todayIso() ? 'Today' : day(d), value: d as string | null }))]} />
          </View>
          <Field label="Approved amount" value={approved} onChangeText={setApproved} keyboardType="decimal-pad" placeholder="14250.50" />
          <Field label="Notes" value={notes} onChangeText={setNotes} multiline style={{ minHeight: 80, textAlignVertical: 'top' }} />
          <Muted>Entering a claim number marks a not-filed claim as open.</Muted>
          <View style={{ flexDirection: 'row', gap: space.sm }}>
            <View style={{ flex: 1 }}><Button title="Cancel" kind="secondary" onPress={() => setEditing(false)} /></View>
            <View style={{ flex: 1 }}><Button title="Save" onPress={save} busy={busy} /></View>
          </View>
        </Card>
      )}

      {c.adjuster && <PersonCard title="Field adjuster" person={c.adjuster} />}
      {c.deskAdjuster && <PersonCard title="Desk adjuster" person={c.deskAdjuster} />}

      {c.carrier && (c.carrier.phone || c.carrier.claimsEmail || c.carrier.website || c.carrier.claimsFax) && (
        <>
          <SectionTitle>Carrier</SectionTitle>
          <Card>
            {c.carrier.phone && <Link icon="call" text={c.carrier.phone} url={`tel:${c.carrier.phone.replace(/[^\d+]/g, '')}`} />}
            {c.carrier.claimsEmail && <Link icon="mail" text={c.carrier.claimsEmail} url={`mailto:${c.carrier.claimsEmail}`} />}
            {c.carrier.website && <Link icon="globe-outline" text={c.carrier.website} url={/^https?:/i.test(c.carrier.website) ? c.carrier.website : `https://${c.carrier.website}`} />}
            {c.carrier.claimsFax && <Muted>{`Fax ${c.carrier.claimsFax}`}</Muted>}
            {c.carrier.preferredSubmission && <Muted>{`Prefers: ${c.carrier.preferredSubmission}`}</Muted>}
          </Card>
        </>
      )}

      {c.supplements.length > 0 && (
        <>
          <SectionTitle>Supplements</SectionTitle>
          <Card>
            {c.supplements.map((s, i) => (
              <Row key={s.id} last={i === c.supplements.length - 1}>
                <View style={{ flexDirection: 'row', gap: space.md }}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.body}>{s.reason}</Text>
                    <Muted>{[label(s.status), s.sentOn ? `sent ${day(s.sentOn)}` : null, s.respondedOn ? `answered ${day(s.respondedOn)}` : null, s.submittedBy]
                      .filter(Boolean).join(' · ')}</Muted>
                    {s.notes && <Muted>{s.notes}</Muted>}
                  </View>
                  <View style={{ alignItems: 'flex-end' }}>
                    {s.requestedAmount != null && <Muted>{`asked ${money(s.requestedAmount)}`}</Muted>}
                    {s.approvedAmount != null && <Text style={styles.body}>{money(s.approvedAmount)}</Text>}
                  </View>
                </View>
              </Row>
            ))}
          </Card>
        </>
      )}

      {feed.view}
    </Screen>
  );
}

function Fact({ name, value }: { name: string; value: string | null }) {
  if (!value) return null;
  return (
    <View style={{ flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 4, gap: space.md }}>
      <Muted>{name}</Muted>
      <Text style={[styles.body, { flexShrink: 1, textAlign: 'right' }]} selectable>{value}</Text>
    </View>
  );
}

function PersonCard({ title, person }: { title: string; person: Person }) {
  return (
    <>
      <SectionTitle>{title}</SectionTitle>
      <Card>
        <Text style={[styles.body, { color: colors.navy, fontWeight: '600' }]} onPress={() => router.push(`/contact/${person.contactId}`)} accessibilityRole="link">
          {person.name}
        </Text>
        <ContactMethods methods={person.methods} />
      </Card>
    </>
  );
}

function Link({ icon, text, url }: { icon: React.ComponentProps<typeof Ionicons>['name']; text: string; url: string }) {
  return (
    <Row onPress={() => Linking.openURL(url)}>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
        <Ionicons name={icon} size={20} color={colors.navy} />
        <Text style={[styles.body, { color: colors.navy, flex: 1 }]} numberOfLines={1}>{text}</Text>
      </View>
    </Row>
  );
}
