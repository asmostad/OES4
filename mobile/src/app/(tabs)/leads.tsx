import { router } from 'expo-router';
import { useEffect, useState } from 'react';
import { Pressable, Text, TextInput, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Button, Card, Loading, Message, Muted, Row, Screen, styles } from '@/components/ui';
import { OpportunityRow, Paged } from '@/lib/api';
import { money } from '@/lib/format';
import { useApi } from '@/lib/session';
import { useCached } from '@/lib/useCached';
import { colors, radius, space } from '@/lib/theme';

/** Open leads: mine by default, searchable. */
export default function Leads() {
  const [q, setQ] = useState('');
  const [search, setSearch] = useState('');
  const [mine, setMine] = useState(true);
  const [pulling, setPulling] = useState(false);
  const { me } = useApi();
  const canCall = me.permissions.includes('leads.prospect');

  // Search as you type, after a short pause.
  useEffect(() => {
    const t = setTimeout(() => setSearch(q.trim()), 350);
    return () => clearTimeout(t);
  }, [q]);

  const query = new URLSearchParams({ outcome: 'open', mine: String(mine), page: '1' });
  if (search) query.set('q', search);
  const leads = useCached<Paged<OpportunityRow>>(`/api/v1/opportunities?${query}`, "Couldn't load leads.");
  const data = leads.data;
  const error = leads.error;
  const load = leads.reload;

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await load(); setPulling(false); }}>
      <OfflineNote offline={leads.offline} savedAt={leads.savedAt} />
      <View style={{ flexDirection: 'row', gap: space.sm }}>
        <View style={{ flex: 1 }}><Button title="New lead" onPress={() => router.push('/new-lead')} /></View>
        <View style={{ flex: 1 }}><Button title="Canvass" kind="secondary" onPress={() => router.push('/canvass')} /></View>
        {canCall && <View style={{ flex: 1 }}><Button title="Call list" kind="secondary" onPress={() => router.push('/calls')} /></View>}
      </View>
      <TextInput value={q} onChangeText={setQ} placeholder="Search name, number, address, phone" placeholderTextColor={colors.muted}
        autoCorrect={false} clearButtonMode="while-editing" style={[styles.input]} accessibilityLabel="Search leads" />
      <View style={{ flexDirection: 'row', gap: space.sm }}>
        {[{ label: 'Mine', value: true }, { label: 'Everyone I can see', value: false }].map((o) => (
          <Pressable key={o.label} onPress={() => setMine(o.value)} accessibilityRole="radio" accessibilityState={{ selected: mine === o.value }}
            style={{ paddingVertical: 6, paddingHorizontal: space.md, borderRadius: radius, borderWidth: 1,
              borderColor: mine === o.value ? colors.navy : colors.border, backgroundColor: mine === o.value ? colors.navy : colors.white }}>
            <Text style={{ color: mine === o.value ? colors.white : colors.text }}>{o.label}</Text>
          </Pressable>
        ))}
      </View>
      <Message text={error} />
      <Card>
        {!data && !error && <Loading />}
        {data?.items.length === 0 && <Muted>No open leads{q ? ' match' : ''}.</Muted>}
        {data?.items.map((o, i) => (
          <Row key={o.id} last={i === data.items.length - 1} onPress={() => router.push(`/opportunity/${o.id}`)}>
            <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: space.sm }}>
              <Text style={[styles.title, { flex: 1 }]} numberOfLines={1}>{o.contactName}</Text>
              {o.expectedValue != null && <Text style={{ color: colors.text, fontWeight: '600' }}>{money(o.expectedValue)}</Text>}
            </View>
            <Muted>{[o.number, o.address].filter(Boolean).join(' · ')}</Muted>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 4 }}>
              <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: o.stageColor || colors.slate }} />
              <Text style={{ color: colors.slate, fontSize: 13 }}>{o.stageName}{o.isStale ? ' · needs attention' : ''}</Text>
            </View>
          </Row>
        ))}
        {data && data.total > data.items.length && <Muted>Showing {data.items.length} of {data.total}. Search to narrow it down.</Muted>}
      </Card>
    </Screen>
  );
}
