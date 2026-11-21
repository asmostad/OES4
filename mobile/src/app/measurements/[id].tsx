import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { Button, Card, Loading, Message, Muted, Row, Screen, SectionTitle, styles } from '@/components/ui';
import { MeasurementSet } from '@/lib/api';
import { openDocument } from '@/lib/files';
import { day, label } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const num = (n: number) => Number(n.toFixed(2)).toLocaleString();

/** One version of a building's measurements for a trade, with what changed from the version before. Saved for offline. */
export default function MeasurementSetScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { api } = useApi();
  const r = useCached<MeasurementSet>(`/api/v1/measurement-sets/${id}`, "Couldn't load these measurements.");
  const [error, setError] = useState<string | null>(null);
  const s = r.data;
  if (!s) return <Screen>{r.error ? <Message text={r.error} /> : <Loading />}</Screen>;

  async function openReport() {
    if (!s?.reportDocumentId) return;
    setError(null);
    try {
      await openDocument(api, { id: s.reportDocumentId, fileName: s.reportFileName ?? 'report.pdf', contentType: 'application/pdf', sizeBytes: 0, category: null, createdAt: '' });
    } catch (x) {
      setError(x instanceof Error ? x.message : "Couldn't open the report.");
    }
  }

  const hasPrevious = Object.keys(s.previous).length > 0;
  return (
    <Screen>
      <Stack.Screen options={{ title: `${s.tradeName} v${s.versionNo}` }} />
      <OfflineNote offline={r.offline} savedAt={r.savedAt} />
      <Card>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
          <View style={{ width: 12, height: 12, borderRadius: 3, backgroundColor: s.tradeColor }} />
          <Text style={[styles.title, { fontSize: 19 }]}>{`${s.tradeName} · ${s.structureName}`}</Text>
        </View>
        <Muted>{[`Version ${s.versionNo}`, s.isCurrent ? 'current' : 'not current', s.sourceLabel, s.measuredOn ? day(s.measuredOn) : null, s.measuredBy]
          .filter(Boolean).join(' · ')}</Muted>
        {s.notes && <Text style={[styles.body, { marginTop: space.sm }]}>{s.notes}</Text>}
        {s.reportDocumentId && <View style={{ marginTop: space.md }}><Button title="Open the report" kind="secondary" onPress={openReport} /></View>}
        {s.canEdit && (
          <View style={{ marginTop: space.sm }}>
            <Button title="Measure again" onPress={() => router.push({ pathname: '/measure', params: { propertyId: s.propertyId, structureId: s.structureId, tradeTypeId: s.tradeTypeId } })} />
          </View>
        )}
      </Card>
      <Message text={error} />

      {s.surfaces.map((surface) => (
        <View key={surface || 'all'}>
          {surface && <SectionTitle>{label(surface)}</SectionTitle>}
          <Card>
            {s.fields.filter((f) => s.values[`${surface}|${f.key}`] != null || s.previous[`${surface}|${f.key}`] != null).map((f, i, all) => {
              const k = `${surface}|${f.key}`;
              const v = s.values[k];
              const before = s.previous[k];
              const changed = hasPrevious && before !== v;
              return (
                <Row key={f.key} last={i === all.length - 1}>
                  <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: space.md }}>
                    <Text style={[styles.body, { flex: 1 }]}>{f.label}</Text>
                    <View style={{ alignItems: 'flex-end' }}>
                      <Text style={[styles.body, { fontWeight: '600' }]}>{v != null ? `${num(v)} ${f.unit}` : '—'}</Text>
                      {changed && <Text style={{ color: colors.warning, fontSize: 12 }}>{before != null ? `was ${num(before)}` : 'new'}</Text>}
                    </View>
                  </View>
                </Row>
              );
            })}
          </Card>
        </View>
      ))}

      {s.versions.length > 1 && (
        <>
          <SectionTitle>Versions</SectionTitle>
          <Card>
            {s.versions.map((v, i) => (
              <Row key={v.id} last={i === s.versions.length - 1} onPress={v.id === s.id ? undefined : () => router.push(`/measurements/${v.id}`)}>
                <Text style={[styles.body, v.id === s.id && { fontWeight: '600' }]}>
                  {[`Version ${v.versionNo}`, v.isCurrent ? 'current' : null, v.sourceLabel, v.measuredOn ? day(v.measuredOn) : null].filter(Boolean).join(' · ')}
                </Text>
              </Row>
            ))}
          </Card>
        </>
      )}
    </Screen>
  );
}
