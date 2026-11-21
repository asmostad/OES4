import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Loading, Message, Muted, Screen, SectionTitle, styles } from '@/components/ui';
import { MeasureField, MeasurementSet, PropertyMeasurements } from '@/lib/api';
import { label, todayIso } from '@/lib/format';
import { outcomeMessage, useOutbox } from '@/lib/outbox';
import { useApi } from '@/lib/session';
import { space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const SOURCES = [{ label: 'Tape on site', value: 'field_tape' }, { label: 'Other', value: 'manual' }];

/**
 * Measurements taken on site: pick the building and trade, then fill in what you measured (a siding job per side).
 * It starts from the current version so you only change what's different; saving makes a new version and keeps the
 * old one. Without a signal it waits in the outbox.
 */
export default function Measure() {
  const params = useLocalSearchParams<{ propertyId: string; structureId?: string; tradeTypeId?: string }>();
  const { api } = useApi();
  const outbox = useOutbox();
  const m = useCached<PropertyMeasurements>(`/api/v1/properties/${params.propertyId}/measurements`, "Couldn't load the measurements.");
  const d = m.data;
  const [structureId, setStructureId] = useState<string | null>(params.structureId ?? null);
  const [tradeId, setTradeId] = useState<string | null>(params.tradeTypeId ?? null);
  const [prefill, setPrefill] = useState<Record<string, Record<string, string>>>({});
  const [edits, setEdits] = useState<Record<string, Record<string, string>>>({});
  const [source, setSource] = useState('field_tape');
  const [notes, setNotes] = useState('');
  const [newStructure, setNewStructure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const structure = structureId ?? d?.structures[0]?.id ?? null;
  const trade = d?.trades.find((t) => t.id === (tradeId ?? d.trades[0]?.id));
  const current = d?.current.find((c) => c.structureId === structure && c.tradeTypeId === trade?.id);

  // Start from the current version's numbers when there is one; what you type is kept per building and trade.
  const combo = `${structure}:${trade?.id}`;
  const currentId = current?.id;
  useEffect(() => {
    if (!currentId) return;
    let live = true;
    api.get<MeasurementSet>(`/api/v1/measurement-sets/${currentId}`)
      .then((s) => { if (live) setPrefill((x) => ({ ...x, [currentId]: Object.fromEntries(Object.entries(s.values).map(([k, v]) => [k, String(v)])) })); })
      .catch(() => { /* offline: start blank */ });
    return () => { live = false; };
  }, [api, currentId]);
  const values: Record<string, string> = { ...(currentId ? prefill[currentId] : {}), ...edits[combo] };

  const groups = useMemo(() => {
    const g = new Map<string, MeasureField[]>();
    for (const f of trade?.fields ?? []) g.set(f.group ?? '', [...(g.get(f.group ?? '') ?? []), f]);
    return [...g.entries()];
  }, [trade]);

  if (!d) return <Screen>{m.error ? <Message text={m.error} /> : <Loading />}</Screen>;
  if (!d.canMeasure) return <Screen><Message text="You don't have permission to take measurements." /></Screen>;
  if (d.trades.length === 0) return <Screen><Message text="No trades take measurements. An admin can set them up on the web." /></Screen>;

  async function addStructure() {
    const name = newStructure?.trim();
    if (!name) return;
    setBusy(true);
    setError(null);
    try {
      await api.json(`/api/v1/properties/${params.propertyId}/structures`, {
        method: 'POST', body: JSON.stringify({ name }), headers: { 'Content-Type': 'application/json', 'Idempotency-Key': `structure-${params.propertyId}-${name}` },
      });
      await m.reload();
      setNewStructure(null);
    } catch (x) {
      setError(x instanceof Error ? x.message : "Couldn't add it.");
    } finally {
      setBusy(false);
    }
  }

  async function save() {
    if (!structure || !trade) return;
    const out: Record<string, number> = {};
    // Only this trade's current fields (an old version may hold a field that's since been retired).
    const allowed = new Set(trade.surfaces.flatMap((s) => trade.fields.map((f) => `${s}|${f.key}`)));
    for (const [k, raw] of Object.entries(values)) {
      if (!allowed.has(k) || !raw.trim()) continue;
      const n = Number(raw.replace(/,/g, ''));
      if (Number.isNaN(n) || n < 0) { setError(`"${raw}" isn't a measurement.`); return; }
      out[k] = n;
    }
    if (Object.keys(out).length === 0) { setError('Enter at least one measurement.'); return; }
    setBusy(true);
    setError(null);
    const s = d!.structures.find((x) => x.id === structure);
    const outcome = await outbox.enqueue({
      kind: 'measurement', method: 'POST', path: `/api/v1/properties/${params.propertyId}/measurements`,
      json: { structureId: structure, tradeTypeId: trade.id, source, measuredOn: todayIso(), notes: notes.trim() || null, values: out },
      label: `${trade.name} measurements for ${s?.name ?? 'a building'}`,
    });
    setBusy(false);
    const msg = outcomeMessage(outcome, 'Measurements saved.');
    if (msg.kind === 'danger') { setError(msg.text); return; }
    router.back();
  }

  const set = (k: string, v: string) => setEdits((x) => ({ ...x, [combo]: { ...x[combo], [k]: v } }));
  const surfaces = trade?.surfaces ?? [''];

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Measure' }} />
      <Muted>{d.address}</Muted>
      <View style={{ gap: space.sm }}>
        <Text style={styles.label}>Building</Text>
        <Chips scroll value={structure} onChange={(v) => setStructureId(v)} options={d.structures.map((s) => ({ label: s.name, value: s.id as string | null }))} />
        {newStructure === null
          ? <Button title="Add a building" kind="secondary" onPress={() => setNewStructure('')} />
          : (
            <Card style={{ gap: space.sm }}>
              <Field label="Building name" value={newStructure} onChangeText={setNewStructure} placeholder="Detached garage" />
              <View style={{ flexDirection: 'row', gap: space.sm }}>
                <View style={{ flex: 1 }}><Button title="Cancel" kind="secondary" onPress={() => setNewStructure(null)} /></View>
                <View style={{ flex: 1 }}><Button title="Add" onPress={addStructure} busy={busy} disabled={!newStructure.trim()} /></View>
              </View>
            </Card>
          )}
      </View>
      <View style={{ gap: space.sm }}>
        <Text style={styles.label}>Trade</Text>
        <Chips scroll value={trade?.id ?? null} onChange={(v) => setTradeId(v)} options={d.trades.map((t) => ({ label: t.name, value: t.id as string | null }))} />
        {current && <Muted>{`Starting from version ${current.versionNo} (${current.sourceLabel}). Saving makes version ${current.versionNo + 1}.`}</Muted>}
      </View>

      {surfaces.map((surface) => (
        <View key={surface || 'all'}>
          {surface && <SectionTitle>{label(surface)}</SectionTitle>}
          {groups.map(([group, fields]) => (
            <Card key={`${surface}-${group}`} style={{ gap: space.sm, marginBottom: space.sm }}>
              {group && !surface && <Text style={styles.label}>{group}</Text>}
              {fields.map((f) => (
                <Field key={f.key} label={`${f.label}${f.unit ? ` (${f.unit})` : ''}`} value={values[`${surface}|${f.key}`] ?? ''}
                  onChangeText={(v) => set(`${surface}|${f.key}`, v)} keyboardType="decimal-pad" />
              ))}
            </Card>
          ))}
        </View>
      ))}

      <View style={{ gap: space.sm }}>
        <Text style={styles.label}>How</Text>
        <Chips value={source} onChange={setSource} options={SOURCES} />
      </View>
      <Field label="Notes" value={notes} onChangeText={setNotes} multiline style={{ minHeight: 60, textAlignVertical: 'top' }} placeholder="Two layers, soft decking by the chimney" />
      <Message text={error} />
      <Button title="Save measurements" onPress={save} busy={busy} />
    </Screen>
  );
}
