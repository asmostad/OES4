import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, Text, View } from 'react-native';

import { Row, styles } from '@/components/ui';
import { ApiError, TaskRow as Task } from '@/lib/api';
import { day, time } from '@/lib/format';
import { useApi } from '@/lib/session';
import { colors, space } from '@/lib/theme';

/** A task with a tick box; ticking it marks it done on the server (and back). Tapping the text opens its lead. */
export function TaskItem({ task, last, onChanged, onError }: { task: Task; last?: boolean; onChanged: () => void; onError: (m: string) => void }) {
  const { api } = useApi();
  const [done, setDone] = useState(task.completedAt !== null);
  const [busy, setBusy] = useState(false);

  async function toggle() {
    if (!task.canEdit || busy) return;
    setBusy(true);
    setDone(!done);
    try {
      await api.put(`/api/v1/tasks/${task.id}/done`, { done: !done });
      onChanged();
    } catch (e) {
      setDone(done);
      onError(e instanceof ApiError ? e.message : "Couldn't update the task.");
    } finally {
      setBusy(false);
    }
  }

  const opensLead = task.entityType === 'opportunity' && task.entityId;
  const due = task.dueLocal ? `${day(task.dueLocal)}${task.hasDueTime ? ' · ' + time(task.dueLocal) : ''}` : 'No due date';

  return (
    <Row last={last}>
      <View style={{ flexDirection: 'row', gap: space.md, alignItems: 'flex-start' }}>
        <Pressable onPress={toggle} hitSlop={12} accessibilityRole="checkbox" accessibilityState={{ checked: done, disabled: !task.canEdit }}>
          <Ionicons name={done ? 'checkbox' : 'square-outline'} size={24} color={task.canEdit ? colors.navy : colors.border} />
        </Pressable>
        <Pressable style={{ flex: 1 }} disabled={!opensLead} onPress={() => opensLead && router.push(`/opportunity/${task.entityId}`)}>
          <Text style={[styles.body, done && { textDecorationLine: 'line-through', color: colors.muted }]}>{task.title}</Text>
          <Text style={{ color: task.isOverdue && !done ? colors.danger : colors.muted, fontSize: 13, marginTop: 2 }}>
            {task.isOverdue && !done ? 'Overdue · ' : ''}{due}{task.priority === 'high' ? ' · High priority' : ''}
          </Text>
        </Pressable>
      </View>
    </Row>
  );
}
