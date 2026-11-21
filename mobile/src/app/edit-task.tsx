import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text, View } from 'react-native';

import { Button, Card, Chips, Field, Message, Screen, styles } from '@/components/ui';
import { ApiError } from '@/lib/api';
import { day, todayIso } from '@/lib/format';
import { useApi } from '@/lib/session';
import { space } from '@/lib/theme';

/** Change a task: what needs doing, notes, priority and the day it's due. */
export default function EditTask() {
  const p = useLocalSearchParams<{ id: string; title: string; notes?: string; due?: string; priority?: string }>();
  const { api } = useApi();
  const [title, setTitle] = useState(p.title ?? '');
  const [notes, setNotes] = useState(p.notes ?? '');
  const [due, setDue] = useState<string | null>(p.due || null);
  const [priority, setPriority] = useState(p.priority === 'high' ? 'high' : 'normal');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const days = [...new Set([...(due ? [due] : []), ...Array.from({ length: 14 }, (_, i) => todayIso(i))])].sort();

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await api.put(`/api/v1/tasks/${p.id}`, { title: title.trim(), notes: notes.trim() || null, dueDate: due, priority });
      router.back();
    } catch (x) {
      setError(x instanceof ApiError || x instanceof Error ? x.message : 'That didn’t save.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Edit task' }} />
      <Card>
        <Field label="What needs doing" value={title} onChangeText={setTitle} />
        <Field label="Notes" value={notes} onChangeText={setNotes} multiline style={{ minHeight: 70, textAlignVertical: 'top' }} />
      </Card>
      <View style={{ gap: space.sm }}>
        <Text style={styles.label}>Due</Text>
        <Chips scroll value={due} onChange={setDue}
          options={[{ label: 'No date', value: null as string | null }, ...days.map((d) => ({ label: d === todayIso() ? 'Today' : day(d), value: d as string | null }))]} />
      </View>
      <Chips value={priority} onChange={setPriority} options={[{ label: 'Normal', value: 'normal' }, { label: 'High priority', value: 'high' }]} />
      <Message text={error} />
      <Button title="Save" onPress={save} busy={busy} disabled={!title.trim()} />
    </Screen>
  );
}
