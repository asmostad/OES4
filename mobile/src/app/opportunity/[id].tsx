import Ionicons from '@expo/vector-icons/Ionicons';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Linking, Platform, Pressable, Text, View } from 'react-native';

import { LeadActions } from '@/components/LeadActions';
import { OfflineNote } from '@/components/OfflineNote';
import { useRecordFeed } from '@/components/RecordFeed';
import { ClaimList, EstimateList } from '@/components/Records';
import { Button, Card, Loading, Message, Muted, Screen, SectionTitle, styles } from '@/components/ui';
import { ClaimItem, EstimateItem, Opportunity } from '@/lib/api';
import { money } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, radius, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const PHONE_KINDS = ['mobile', 'phone', 'work', 'home', 'office'];
const newKey = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

export default function OpportunityScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  // Each part is saved for offline; the lead's own details decide what the screen shows.
  const lead = useCached<Opportunity>(`/api/v1/opportunities/${id}`, "Couldn't load this lead.");
  const opp = lead.data;
  const feed = useRecordFeed('opportunity', id, opp?.number ?? 'a lead', 'Add to this lead');
  const estimates = useCached<EstimateItem[]>(`/api/v1/estimates?opportunityId=${id}`, "Couldn't load the estimates.");
  const claims = useCached<ClaimItem[]>(`/api/v1/claims?opportunityId=${id}`, "Couldn't load the claim.");
  const { api, me } = useApi();
  const canEstimate = me.permissions.includes('estimates.create') || me.permissions.includes('estimates.manage');
  const canAsk = me.permissions.includes('ai.assistant');
  const [asking, setAsking] = useState(false);

  async function ask() {
    setAsking(true);
    try {
      const { id: convo } = await api.json<{ id: string }>('/api/v1/assistant/conversations', {
        method: 'POST', body: JSON.stringify({ entityType: 'opportunity', entityId: id }),
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': newKey() },
      });
      router.push(`/assistant/${convo}`);
    } catch (x) {
      setActionError(x instanceof Error ? x.message : "Couldn't start the assistant.");
    } finally {
      setAsking(false);
    }
  }
  const error = lead.error;
  const [actionError, setActionError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const { reload: reloadLead } = lead;
  const { reload: reloadFeed } = feed;
  const { reload: reloadEstimates } = estimates;
  const { reload: reloadClaims } = claims;
  const load = useCallback(async () => {
    await Promise.all([reloadLead(), reloadFeed(), reloadEstimates(), reloadClaims()]);
  }, [reloadLead, reloadFeed, reloadEstimates, reloadClaims]);

  if (!opp) return <Screen>{error ? <Message text={error} /> : <Loading />}</Screen>;

  const phone = opp.contactMethods.find((m) => PHONE_KINDS.includes(m.kind));
  const email = opp.contactMethods.find((m) => m.kind === 'email');
  const digits = phone?.value.replace(/[^\d+]/g, '');
  const maps = opp.address
    ? Platform.select({ ios: `maps:0,0?q=${encodeURIComponent(opp.address)}`, android: `geo:0,0?q=${encodeURIComponent(opp.address)}`,
      default: `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(opp.address)}` })
    : null;

  return (
    <Screen refreshing={refreshing} onRefresh={async () => { setRefreshing(true); await load(); setRefreshing(false); }}>
      <Stack.Screen options={{ title: opp.number }} />
      <OfflineNote offline={lead.offline} savedAt={lead.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 20, color: colors.navy }]} onPress={() => router.push(`/contact/${opp.contactId}`)} accessibilityRole="link">{opp.contactName}</Text>
        <Muted>{opp.name}</Muted>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: space.sm }}>
          <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: opp.stageColor || colors.slate }} />
          <Text style={{ color: colors.slate }}>{opp.stageName}{opp.expectedValue != null ? ` · ${money(opp.expectedValue)}` : ''}</Text>
        </View>
        {opp.address && <Text style={[styles.body, { marginTop: space.sm, color: colors.navy }]} onPress={opp.propertyId ? () => router.push(`/property/${opp.propertyId}`) : undefined} accessibilityRole="link">{opp.address}</Text>}
        {opp.ownerName && <Muted>Owner: {opp.ownerName}</Muted>}
        <View style={{ flexDirection: 'row', gap: space.sm, marginTop: space.md }}>
          <Action icon="call" text="Call" onPress={digits ? () => Linking.openURL(`tel:${digits}`) : undefined} />
          <Action icon="chatbubble" text="Text" onPress={digits ? () => Linking.openURL(`sms:${digits}`) : undefined} />
          <Action icon="mail" text="Email" onPress={email ? () => Linking.openURL(`mailto:${email.value}`) : undefined} />
          <Action icon="navigate" text="Directions" onPress={maps ? () => Linking.openURL(maps) : undefined} />
        </View>
        {opp.description && <Text style={[styles.body, { marginTop: space.md }]}>{opp.description}</Text>}
      </Card>

      <Message text={actionError} />
      <Message text={notice} kind="success" />

      {canAsk && <Button title="Ask the assistant about this lead" kind="secondary" onPress={ask} busy={asking} />}

      <LeadActions opp={opp} onSent={load} onMessage={(text, kind) => {
        if (kind === 'success') { setNotice(text); setActionError(null); } else { setActionError(text); setNotice(null); }
      }} />

      {(claims.data?.length ?? 0) > 0 && (
        <>
          <SectionTitle>Insurance claim</SectionTitle>
          <ClaimList claims={claims.data!} />
        </>
      )}

      {estimates.data && (estimates.data.length > 0 || canEstimate) && (
        <>
          <SectionTitle>Estimates</SectionTitle>
          <EstimateList estimates={estimates.data} empty="No estimates yet. Build one on the web." />
        </>
      )}

      {feed.view}
    </Screen>
  );
}

function Action({ icon, text, onPress }: { icon: React.ComponentProps<typeof Ionicons>['name']; text: string; onPress?: () => void }) {
  return (
    <Pressable onPress={onPress} disabled={!onPress} accessibilityRole="button" accessibilityLabel={text}
      style={({ pressed }) => ({ flex: 1, alignItems: 'center', paddingVertical: space.sm, borderRadius: radius,
        backgroundColor: onPress ? colors.cloud : colors.white, opacity: onPress ? (pressed ? 0.6 : 1) : 0.35 })}>
      <Ionicons name={icon} size={22} color={colors.navy} />
      <Text style={{ color: colors.navy, fontSize: 12, marginTop: 2 }}>{text}</Text>
    </Pressable>
  );
}
