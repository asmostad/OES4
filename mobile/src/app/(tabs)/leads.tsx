import { router, useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { Pressable, Text, TextInput, View } from 'react-native';

import { Card, Loading, Message, Muted, Row, Screen, styles } from '@/components/ui';
import { ApiError, OpportunityRow, Paged } from '@/lib/api';
import { money } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, radius, space } from '@/lib/theme';

/** Open leads: mine by default, searchable. */
export default function Leads() {
  const { api } = useApi();
  const [q, setQ] = useState('');
  const [mine, setMine] = useState(true);
  const [data, setData] = useState<Paged<OpportunityRow> | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const query = new URLSearchParams({ outcome: 'open', mine: String(mine), page: '1' });
      if (q.trim()) query.set('q', q.trim());
      setData(await api.get<Paged<OpportunityRow>>(`/api/v1/opportunities?${query}`));
      setError(null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't load leads.");
    }
  }, [api, q, mine]);

  useFocusEffect(useCallback(() => { load(); }, [load]));
  // Search as you type, after a short pause.
  useEffect(() => {
    const t = setTimeout(load, 350);
    return () => clearTimeout(t);
  }, [load]);

  return (
    <Screen refreshing={refreshing} onRefresh={async () => { setRefreshing(true); await load(); setRefreshing(false); }}>
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
