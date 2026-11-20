import { router } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Muted, SectionTitle } from '@/components/ui';
import { Opportunity, Options } from '@/lib/api';
import { todayIso } from '@/lib/format';
import { outcomeMessage, useOutbox } from '@/lib/outbox';
import { colors, space } from '@/lib/theme';
import { useCached } from '@/lib/useCached';

const DUE = [{ label: 'Today', value: 0 }, { label: 'Tomorrow', value: 1 }, { label: 'Next week', value: 7 }, { label: 'No date', value: -1 }];

/**
 * On a lead: move it to another stage (lost asks why), add a task on it, book an appointment. All through the
 * outbox, so they work offline and send when the connection's back.
 */
export function LeadActions({ opp, onSent, onMessage }: {
  opp: Opportunity; onSent: () => void; onMessage: (text: string, kind: 'success' | 'danger') => void;
}) {
  const outbox = useOutbox();
  const options = useCached<Options>('/api/v1/options', "Couldn't load the stages.");
  const stages = options.data?.stages ?? [];
  const [stageOpen, setStageOpen] = useState(false);
  const [stageId, setStageId] = useState<string | null>(null);
  const [reason, setReason] = useState<string | null>(null);
  const [task, setTask] = useState('');
  const [due, setDue] = useState(1);
  const [busy, setBusy] = useState<'stage' | 'task' | null>(null);

  const chosen = stages.find((s) => s.id === stageId);
  const current = stages.find((s) => s.name === opp.stageName);

  async function moveStage() {
    if (!chosen) return;
    if (chosen.outcome === 'lost' && !reason) return onMessage('Pick why it was lost.', 'danger');
    setBusy('stage');
    const outcome = await outbox.enqueue({
      kind: 'stage', method: 'PUT', path: `/api/v1/opportunities/${opp.id}/stage`,
      json: { stageId: chosen.id, lostReasonKey: chosen.outcome === 'lost' ? reason : null }, label: `${opp.number} → ${chosen.name}`,
    });
    setBusy(null);
    const m = outcomeMessage(outcome, `Moved to ${chosen.name}.`);
    onMessage(m.text, m.kind);
    if (m.kind === 'success') {
      setStageOpen(false);
      setStageId(null);
      setReason(null);
      if (outcome.status === 'sent') onSent();
    }
  }

  async function addTask() {
    if (!task.trim()) return;
    setBusy('task');
    const outcome = await outbox.enqueue({
      kind: 'task', method: 'POST', path: '/api/v1/tasks',
      json: { title: task.trim(), dueDate: due < 0 ? null : todayIso(due), entityType: 'opportunity', entityId: opp.id },
      label: `New task on ${opp.number}: ${task.trim()}`,
    });
    setBusy(null);
    const m = outcomeMessage(outcome, 'Task added.');
    onMessage(m.text, m.kind);
    if (m.kind === 'success') setTask('');
  }

  return (
    <>
      <SectionTitle>Next steps</SectionTitle>
      <Card style={{ gap: space.md }}>
        <Button title="Book appointment" onPress={() => router.push(`/book/${opp.id}`)} />
        {opp.canEdit && !stageOpen && <Button title={`Change stage (now ${opp.stageName})`} kind="secondary" onPress={() => setStageOpen(true)} />}
        {stageOpen && (
          <View style={{ gap: space.sm }}>
            <Text style={{ fontWeight: '600', color: colors.text }}>Move to</Text>
            {stages.length === 0 && <Muted>Loading stages…</Muted>}
            <Chips value={stageId} onChange={(v) => { setStageId(v); setReason(null); }}
              options={stages.filter((s) => s.id !== current?.id).map((s) => ({ label: s.outcome === 'open' ? s.name : `${s.name} (${s.outcome})`, value: s.id as string | null }))} />
            {chosen?.outcome === 'lost' && (
              <>
                <Text style={{ fontWeight: '600', color: colors.text }}>Why was it lost?</Text>
                <Chips value={reason} onChange={setReason}
                  options={(options.data?.lostReasons ?? []).map((r) => ({ label: r.label, value: r.value as string | null }))} />
              </>
            )}
            <View style={{ flexDirection: 'row', gap: space.sm }}>
              <View style={{ flex: 1 }}><Button title={chosen ? `Move to ${chosen.name}` : 'Pick a stage'} onPress={moveStage} disabled={!chosen} busy={busy === 'stage'} /></View>
              <View style={{ flex: 1 }}><Button title="Cancel" kind="secondary" onPress={() => { setStageOpen(false); setStageId(null); }} /></View>
            </View>
          </View>
        )}
        <View>
          <Field label="Task on this lead" value={task} onChangeText={setTask} placeholder="Send the adjuster photos" />
          <Chips value={due} onChange={setDue} options={DUE} />
          <View style={{ marginTop: space.md }}>
            <Button title="Add task" kind="secondary" onPress={addTask} busy={busy === 'task'} disabled={!task.trim()} />
          </View>
        </View>
      </Card>
    </>
  );
}
