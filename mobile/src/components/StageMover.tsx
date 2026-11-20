import { useEffect, useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Message, Muted, Row, SectionTitle, styles } from '@/components/ui';
import { MovePreview, StageHistory, WorkStage } from '@/lib/api';
import { when } from '@/lib/format';
import { outcomeMessage, useOutbox } from '@/lib/outbox';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';

/**
 * Move a project or trade to another workflow stage, with an optional note. For trades the server is asked first
 * what the move would do (a gate that blocks it, or automatic follow-ups), when there's a signal. The move itself
 * goes through the outbox, so it's kept and sent later without one; a gate that fails then shows in the outbox.
 */
export function StageMover({ kind, id, label, current, stages, history, canAdvance, onSent }: {
  kind: 'trades' | 'projects'; id: string; label: string; current: WorkStage; stages: WorkStage[]; history: StageHistory[];
  canAdvance: boolean; onSent: () => void;
}) {
  const { api } = useApi();
  const outbox = useOutbox();
  const [open, setOpen] = useState(false);
  const [stageId, setStageId] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const [preview, setPreview] = useState<MovePreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ text: string; kind: 'success' | 'danger' } | null>(null);
  const chosen = stages.find((s) => s.id === stageId);

  useEffect(() => {
    if (kind !== 'trades' || !stageId) return;
    let live = true;
    api.get<MovePreview>(`/api/v1/trades/${id}/stage-preview?stageId=${stageId}`)
      .then((p) => { if (live) setPreview(p); })
      .catch(() => { if (live) setPreview(null); }); // offline: move anyway, the server checks when it's sent
    return () => { live = false; };
  }, [api, kind, id, stageId]);

  function pick(v: string | null) {
    setPreview(null);
    setStageId(v);
  }

  async function move() {
    if (!chosen) return;
    setBusy(true);
    const outcome = await outbox.enqueue({
      kind: 'stage', method: 'PUT', path: `/api/v1/${kind}/${id}/stage`, json: { stageId: chosen.id, note: note.trim() || null },
      label: `${label} → ${chosen.name}`,
    });
    setBusy(false);
    const m = outcomeMessage(outcome, `Moved to ${chosen.name}.`);
    setMessage(m);
    if (m.kind === 'success') {
      setOpen(false);
      pick(null);
      setNote('');
      if (outcome.status === 'sent') onSent();
    }
  }

  return (
    <>
      <SectionTitle>Stage</SectionTitle>
      <Card style={{ gap: space.md }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
          <View style={{ width: 12, height: 12, borderRadius: 6, backgroundColor: current.color || colors.slate }} />
          <Text style={[styles.title, { fontSize: 17 }]}>{current.name}</Text>
        </View>
        {message && <Message text={message.text} kind={message.kind} />}
        {canAdvance && !open && <Button title="Move to another stage" kind="secondary" onPress={() => { setOpen(true); setMessage(null); }} />}
        {!canAdvance && <Muted>You can see this job; moving it needs the workflow permission.</Muted>}
        {open && (
          <View style={{ gap: space.sm }}>
            <Chips value={stageId} onChange={pick}
              options={stages.filter((s) => s.id !== current.id).map((s) => ({ label: s.name, value: s.id as string | null }))} />
            {preview?.blocker && <Message text={preview.blocker} />}
            {preview && preview.automations.length > 0 && <Muted>{`Also: ${preview.automations.join('; ')}`}</Muted>}
            <Field label="Note (optional)" value={note} onChangeText={setNote} placeholder="Tear-off done, dry-in tonight" />
            <View style={{ flexDirection: 'row', gap: space.sm }}>
              <View style={{ flex: 1 }}>
                <Button title={chosen ? `Move to ${chosen.name}` : 'Pick a stage'} onPress={move} busy={busy}
                  disabled={!chosen || preview?.allowed === false} />
              </View>
              <View style={{ flex: 1 }}><Button title="Cancel" kind="secondary" onPress={() => { setOpen(false); pick(null); }} /></View>
            </View>
          </View>
        )}
      </Card>
      {history.length > 0 && (
        <>
          <SectionTitle>Stage history</SectionTitle>
          <Card>
            {history.slice(0, 10).map((h, i) => (
              <Row key={`${h.at}-${i}`} last={i === Math.min(history.length, 10) - 1}>
                <Text style={styles.body}>{h.from ? `${h.from} → ${h.to}` : h.to}</Text>
                <Muted>{[when(h.at), h.isAutomatic ? 'automatic' : h.by].filter(Boolean).join(' · ')}</Muted>
                {h.note && <Text style={styles.body}>{h.note}</Text>}
              </Row>
            ))}
          </Card>
        </>
      )}
    </>
  );
}
