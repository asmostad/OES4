import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { Linking, Platform, Text, View } from 'react-native';

import { Card, Muted, Row, styles } from '@/components/ui';
import { ClaimItem, ContactMethod, EstimateItem, OpportunityBrief, WorkOrderItem } from '@/lib/api';
import { day, label, money } from '@/lib/format';
import { colors, space } from '@/lib/theme';

/** Leads as tappable rows, with their stage. */
export function LeadList({ leads, empty = 'No leads.' }: { leads: OpportunityBrief[]; empty?: string }) {
  return (
    <Card>
      {leads.length === 0 && <Muted>{empty}</Muted>}
      {leads.map((o, i) => (
        <Row key={o.id} last={i === leads.length - 1} onPress={() => router.push(`/opportunity/${o.id}`)}>
          <Text style={styles.body} numberOfLines={1}>{o.name}</Text>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 2 }}>
            <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: o.stageColor || colors.slate }} />
            <Muted>{[o.number, o.stageName, o.outcome !== 'open' ? label(o.outcome) : null].filter(Boolean).join(' · ')}</Muted>
          </View>
        </Row>
      ))}
    </Card>
  );
}

/** Status colours for estimates, matching the web's badges. */
export const ESTIMATE_COLORS: Record<string, string> = {
  draft: '#64748b', issued: '#2563eb', signed: '#16a34a', declined: '#dc2626', shelved: '#94a3b8', superseded: '#94a3b8',
};

/** Estimates as tappable rows: number, status and total. */
export function EstimateList({ estimates, empty = 'No estimates yet.' }: { estimates: EstimateItem[]; empty?: string }) {
  return (
    <Card>
      {estimates.length === 0 && <Muted>{empty}</Muted>}
      {estimates.map((e, i) => (
        <Row key={e.id} last={i === estimates.length - 1} onPress={() => router.push(`/estimate/${e.id}`)}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
            <View style={{ flex: 1 }}>
              <Text style={styles.body} numberOfLines={1}>{e.name || e.number}</Text>
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 2 }}>
                <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: ESTIMATE_COLORS[e.status] ?? colors.slate }} />
                <Muted>{[e.number, label(e.status), e.latestVersionNo > 0 ? `v${e.latestVersionNo}` : null,
                  e.pendingApprovals > 0 ? `${e.pendingApprovals} waiting for approval` : null].filter(Boolean).join(' · ')}</Muted>
              </View>
            </View>
            <Text style={[styles.body, { fontWeight: '600' }]}>{money(e.total)}</Text>
          </View>
        </Row>
      ))}
    </Card>
  );
}

export const WORK_ORDER_COLORS: Record<string, string> = {
  draft: '#64748b', issued: '#2563eb', completed: '#16a34a', paid: '#0f766e', cancelled: '#94a3b8',
};

/** A trade's work orders: the sub, status, whether they accepted, the date and amount. */
export function WorkOrderList({ workOrders }: { workOrders: WorkOrderItem[] }) {
  return (
    <Card>
      {workOrders.map((w, i) => (
        <Row key={w.id} last={i === workOrders.length - 1} onPress={() => router.push(`/work-order/${w.id}`)}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
            <View style={{ flex: 1 }}>
              <Text style={styles.body} numberOfLines={1}>{w.subName}</Text>
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 2 }}>
                <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: WORK_ORDER_COLORS[w.status] ?? colors.slate }} />
                <Muted>{[w.number, label(w.status), w.status === 'issued' ? (w.acceptStatus === 'pending' ? 'not accepted yet' : w.acceptStatus) : null,
                  w.workDate ? day(w.workDate) : null].filter(Boolean).join(' · ')}</Muted>
              </View>
            </View>
            <Text style={styles.body}>{money(w.amount)}</Text>
          </View>
        </Row>
      ))}
    </Card>
  );
}

export const CLAIM_COLORS: Record<string, string> = {
  not_filed: '#64748b', open: '#2563eb', approved: '#16a34a', denied: '#dc2626', closed: '#94a3b8',
};

/** A lead's insurance claims: carrier, claim number, status, and the inspection day when set. */
export function ClaimList({ claims }: { claims: ClaimItem[] }) {
  return (
    <Card>
      {claims.map((c, i) => (
        <Row key={c.id} last={i === claims.length - 1} onPress={() => router.push(`/claim/${c.id}`)}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
            <View style={{ flex: 1 }}>
              <Text style={styles.body} numberOfLines={1}>{c.carrierName ?? 'Carrier not set'}{c.claimNumber ? ` · ${c.claimNumber}` : ''}</Text>
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 2 }}>
                <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: CLAIM_COLORS[c.status] ?? colors.slate }} />
                <Muted>{[label(c.status), c.inspectionOn ? `inspection ${day(c.inspectionOn)}` : null,
                  c.openSupplements > 0 ? `${c.openSupplements} open supplement${c.openSupplements === 1 ? '' : 's'}` : null].filter(Boolean).join(' · ')}</Muted>
              </View>
            </View>
            {c.approvedAmount != null && <Text style={styles.body}>{money(c.approvedAmount)}</Text>}
          </View>
        </Row>
      ))}
    </Card>
  );
}

const PHONE_KINDS = ['mobile', 'phone', 'work', 'home', 'office'];

/** Each phone and email, with call / text / email buttons. */
export function ContactMethods({ methods }: { methods: ContactMethod[] }) {
  if (methods.length === 0) return <Muted>No phone or email on file.</Muted>;
  return (
    <View>
      {methods.map((m, i) => {
        const phone = PHONE_KINDS.includes(m.kind);
        const digits = m.value.replace(/[^\d+]/g, '');
        return (
          <Row key={`${m.kind}-${m.value}`} last={i === methods.length - 1}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
              <View style={{ flex: 1 }}>
                <Text style={styles.body}>{m.value}</Text>
                <Muted>{label(m.kind)}{m.isPrimary ? ' · primary' : ''}</Muted>
              </View>
              {phone ? (
                <>
                  <Ionicons name="call" size={22} color={colors.navy} onPress={() => Linking.openURL(`tel:${digits}`)} accessibilityLabel={`Call ${m.value}`} />
                  <Ionicons name="chatbubble" size={22} color={colors.navy} onPress={() => Linking.openURL(`sms:${digits}`)} accessibilityLabel={`Text ${m.value}`} />
                </>
              ) : m.kind === 'email' ? (
                <Ionicons name="mail" size={22} color={colors.navy} onPress={() => Linking.openURL(`mailto:${m.value}`)} accessibilityLabel={`Email ${m.value}`} />
              ) : null}
            </View>
          </Row>
        );
      })}
    </View>
  );
}

/** The phone's maps app, searching for the address. */
export function openMaps(address: string) {
  const q = encodeURIComponent(address);
  Linking.openURL(Platform.select({ ios: `maps:0,0?q=${q}`, android: `geo:0,0?q=${q}`, default: `https://www.google.com/maps/search/?api=1&query=${q}` }));
}
