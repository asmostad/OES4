import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { Linking, Platform, Text, View } from 'react-native';

import { Card, Muted, Row, styles } from '@/components/ui';
import { ContactMethod, EstimateItem, OpportunityBrief } from '@/lib/api';
import { label, money } from '@/lib/format';
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
