import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { ContactMethods, openMaps, WorkOrderList } from '@/components/Records';
import { useRecordFeed } from '@/components/RecordFeed';
import { StageMover } from '@/components/StageMover';
import { Button, Card, Loading, Message, Muted, Screen, SectionTitle, styles } from '@/components/ui';
import { TradeDetail, WorkOrderItem } from '@/lib/api';
import { day } from '@/lib/format';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

/** One trade on a job (roof, gutters...): the dates, the crew note, the stage, and notes and photos from the site. */
export default function TradeScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const r = useCached<TradeDetail>(`/api/v1/trades/${id}`, "Couldn't load this trade.");
  const t = r.data;
  const what = t ? `${t.projectNumber} ${t.name}` : 'a trade';
  const feed = useRecordFeed('trade', id, what, 'From the site');
  const workOrders = useCached<WorkOrderItem[]>(`/api/v1/work-orders?tradeId=${id}`, "Couldn't load the work orders.");
  const [pulling, setPulling] = useState(false);
  const { reload } = r;
  const { reload: reloadFeed } = feed;
  const { reload: reloadWorkOrders } = workOrders;
  const load = useCallback(async () => { await Promise.all([reload(), reloadFeed(), reloadWorkOrders()]); }, [reload, reloadFeed, reloadWorkOrders]);

  if (!t) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;
  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await load(); setPulling(false); }}>
      <Stack.Screen options={{ title: t.name }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
          <View style={{ width: 12, height: 12, borderRadius: 3, backgroundColor: t.color }} />
          <Text style={[styles.title, { fontSize: 20 }]}>{t.name}</Text>
        </View>
        <Text style={[styles.body, { color: colors.navy, marginTop: space.xs }]} onPress={() => router.push(`/project/${t.projectId}`)} accessibilityRole="link">
          {t.projectNumber} · {t.projectName}
        </Text>
        <Text style={[styles.body, { marginTop: space.sm, color: colors.navy }]} onPress={() => router.push(`/contact/${t.contactId}`)} accessibilityRole="link">
          {t.contactName}
        </Text>
        {t.address && <Text style={styles.body}>{t.address}</Text>}
        {t.isRemoved && <Message text="Removed from the board." />}
        {t.address && <View style={{ marginTop: space.md }}><Button title="Directions" kind="secondary" onPress={() => openMaps(t.address!)} /></View>}
      </Card>

      <SectionTitle>The work</SectionTitle>
      <Card style={{ gap: space.sm }}>
        <Text style={styles.body}>Work date: {t.workDate ? day(t.workDate) : 'not scheduled'}</Text>
        <Text style={styles.body}>Materials: {t.materialDate ? day(t.materialDate) : 'no date yet'}</Text>
        {t.crewNote && (
          <View>
            <Text style={styles.label}>Crew note</Text>
            <Text style={styles.body}>{t.crewNote}</Text>
          </View>
        )}
        {t.description && <Muted>{t.description}</Muted>}
      </Card>
      <Card><ContactMethods methods={t.contactMethods} /></Card>

      {(workOrders.data?.length ?? 0) > 0 && (
        <>
          <SectionTitle>Work orders</SectionTitle>
          <WorkOrderList workOrders={workOrders.data!} />
        </>
      )}

      <StageMover kind="trades" id={t.id} label={what} current={t.stage} stages={t.stages} history={t.history}
        canAdvance={t.canAdvance && !t.isRemoved} onSent={load} />

      {feed.view}
    </Screen>
  );
}
