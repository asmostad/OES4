import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { ContactMethods, openMaps } from '@/components/Records';
import { useRecordFeed } from '@/components/RecordFeed';
import { StageMover } from '@/components/StageMover';
import { Button, Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { ChangeOrderList, ProjectDetail } from '@/lib/api';
import { cents, day, label } from '@/lib/format';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/** A project: the customer, the address, its trades, the stage, and notes, photos and documents. Saved for offline. */
export default function ProjectScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const r = useCached<ProjectDetail>(`/api/v1/projects/${id}`, "Couldn't load this project.");
  const p = r.data;
  const feed = useRecordFeed('project', id, p?.number ?? 'a project', 'Add to this project');
  const changes = useCached<ChangeOrderList>(`/api/v1/projects/${id}/change-orders`, "Couldn't load the change orders.");
  const co = changes.data;
  const [pulling, setPulling] = useState(false);
  const { reload } = r;
  const { reload: reloadFeed } = feed;
  const { reload: reloadChanges } = changes;
  const load = useCallback(async () => { await Promise.all([reload(), reloadFeed(), reloadChanges()]); }, [reload, reloadFeed, reloadChanges]);

  if (!p) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;
  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await load(); setPulling(false); }}>
      <Stack.Screen options={{ title: p.number }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        <Text style={[styles.title, { fontSize: 20, color: colors.navy }]} onPress={() => router.push(`/contact/${p.contactId}`)} accessibilityRole="link">
          {p.contactName}
        </Text>
        <Muted>{p.name}</Muted>
        {p.address && <Text style={[styles.body, { marginTop: space.sm }]}>{p.address}</Text>}
        {p.ownerName && <Muted>Project manager: {p.ownerName}</Muted>}
        {p.isRemoved && <Message text="Removed from the board." />}
        {p.notes && <Text style={[styles.body, { marginTop: space.md }]}>{p.notes}</Text>}
        <View style={{ flexDirection: 'row', gap: space.sm, marginTop: space.md }}>
          {p.address && <View style={{ flex: 1 }}><Button title="Directions" kind="secondary" onPress={() => openMaps(p.address!)} /></View>}
          <View style={{ flex: 1 }}><Button title="Open lead" kind="secondary" onPress={() => router.push(`/opportunity/${p.opportunityId}`)} /></View>
        </View>
      </Card>
      <Card><ContactMethods methods={p.contactMethods} /></Card>

      <SectionTitle>Trades</SectionTitle>
      <Card>
        {p.trades.length === 0 && <Muted>No trades.</Muted>}
        {p.trades.map((t, i) => (
          <Row key={t.id} last={i === p.trades.length - 1} onPress={() => router.push(`/trade/${t.id}`)}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
              <View style={{ width: 4, alignSelf: 'stretch', borderRadius: 2, backgroundColor: t.color }} />
              <View style={{ flex: 1 }}>
                <Text style={styles.body}>{t.name}</Text>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 2 }}>
                  <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: t.stageColor }} />
                  <Muted>{[t.stageName, t.workDate ? `work ${day(t.workDate)}` : null, t.isStale ? 'needs attention' : null].filter(Boolean).join(' · ')}</Muted>
                </View>
              </View>
            </View>
          </Row>
        ))}
      </Card>

      <StageMover kind="projects" id={p.id} label={p.number} current={p.stage} stages={p.stages} history={p.history}
        canAdvance={p.canAdvance && !p.isRemoved} onSent={load} />

      {co && (co.items.length > 0 || co.canAdd) && (
        <>
          <SectionTitle>Change orders</SectionTitle>
          <Card>
            {co.items.length === 0 && <Muted>None yet.</Muted>}
            {co.items.map((c, i) => (
              <Row key={c.id} last={i === co.items.length - 1} onPress={() => router.push(`/change-order/${c.id}`)}>
                <View style={{ flexDirection: 'row', gap: space.md, alignItems: 'center' }}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.body} numberOfLines={2}>{`#${c.sequence} ${c.description}`}</Text>
                    <Muted>{[label(c.status), c.approvedByName ? `by ${c.approvedByName}` : null, c.isInsuranceSupplement ? 'supplement' : null].filter(Boolean).join(' · ')}</Muted>
                  </View>
                  <Text style={[styles.body, c.status === 'rejected' && { color: colors.muted, textDecorationLine: 'line-through' }]}>{cents(c.amount)}</Text>
                </View>
              </Row>
            ))}
            <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginTop: space.sm }}>
              <Muted>Contract with approved changes</Muted>
              <Text style={[styles.body, { fontWeight: '600' }]}>{cents(co.contractValue)}</Text>
            </View>
          </Card>
          {co.canAdd && <Button title="Add a change order" kind="secondary" onPress={() => router.push({ pathname: '/new-change-order', params: { projectId: id } })} />}
        </>
      )}

      {feed.view}
    </Screen>
  );
}
