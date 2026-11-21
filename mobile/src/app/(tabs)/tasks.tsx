import Ionicons from '@expo/vector-icons/Ionicons';
import { useState } from 'react';
import { Pressable, Text, View } from 'react-native';

import { OfflineNote } from '@/components/OfflineNote';
import { OutboxBanner } from '@/components/OutboxBanner';
import { TaskItem } from '@/components/TaskRow';
import { Button, Card, Field, Loading, Message, Muted, Screen, SectionTitle } from '@/components/ui';
import { TaskRow } from '@/lib/api';
import { todayIso } from '@/lib/format';
import { outcomeMessage, useOutbox } from '@/lib/outbox';
import { useCached } from '@/lib/useCached';
import { colors, radius, space } from '@/lib/theme';

const DUE = [
  { label: 'Today', days: 0 },
  { label: 'Tomorrow', days: 1 },
  { label: 'Next week', days: 7 },
  { label: 'No date', days: null },
] as const;

/** My open tasks, and adding a new one. */
export default function Tasks() {
  const list = useCached<TaskRow[]>('/api/v1/tasks?mine=true&done=false', "Couldn't load your tasks.");
  const tasks = list.data;
  const load = list.reload;
  const [pulling, setPulling] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [due, setDue] = useState<number | null>(0);
  const [busy, setBusy] = useState(false);

  const outbox = useOutbox();
  const waitingTasks = outbox.items.filter((i) => i.kind === 'task');

  async function add() {
    if (!title.trim()) return setError('What needs doing?');
    setBusy(true);
    setError(null);
    const outcome = await outbox.enqueue({
      kind: 'task', method: 'POST', path: '/api/v1/tasks', json: { title: title.trim(), dueDate: due === null ? null : todayIso(due) },
      label: `New task: ${title.trim()}`,
    });
    const m = outcomeMessage(outcome, 'Task added.');
    if (m.kind === 'success') {
      setTitle('');
      setSaved(m.text);
      if (outcome.status === 'sent') await load();
    } else setError(m.text);
    setBusy(false);
  }

  return (
    <Screen refreshing={pulling} onRefresh={async () => { setPulling(true); await load(); setPulling(false); }}>
      <OfflineNote offline={list.offline} savedAt={list.savedAt} />
      <Card>
        <Field label="New task" value={title} onChangeText={(t) => { setTitle(t); setSaved(null); }} placeholder="Call the adjuster" returnKeyType="done" onSubmitEditing={add} />
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.sm, marginBottom: space.md }}>
          {DUE.map((d) => (
            <Pressable key={d.label} onPress={() => setDue(d.days)} accessibilityRole="radio" accessibilityState={{ selected: due === d.days }}
              style={{ paddingVertical: 6, paddingHorizontal: space.md, borderRadius: radius, borderWidth: 1,
                borderColor: due === d.days ? colors.navy : colors.border, backgroundColor: due === d.days ? colors.navy : colors.white }}>
              <Text style={{ color: due === d.days ? colors.white : colors.text }}>{d.label}</Text>
            </Pressable>
          ))}
        </View>
        <Button title="Add task" onPress={add} busy={busy} />
      </Card>
      <Message text={error ?? list.error} />
      <Message text={saved} kind="success" />
      <OutboxBanner />
      <SectionTitle>Open tasks</SectionTitle>
      <Card>
        {waitingTasks.map((w) => (
          <View key={w.key} style={{ paddingVertical: space.md, flexDirection: 'row', gap: space.md, opacity: 0.7 }}>
            <Ionicons name="cloud-upload-outline" size={22} color={w.error ? colors.danger : colors.warning} />
            <View style={{ flex: 1 }}>
              <Text style={{ color: colors.text }}>{(w.json as { title: string }).title}</Text>
              <Muted>{w.error ? 'Couldn’t be sent' : 'Waiting to send'}</Muted>
            </View>
          </View>
        ))}
        {!tasks && !list.error && <Loading />}
        {tasks?.length === 0 && <Muted>No open tasks.</Muted>}
        {tasks?.map((t, i) => <TaskItem key={t.id} task={t} last={i === tasks.length - 1} onChanged={load} onError={setError} />)}
      </Card>
    </Screen>
  );
}
