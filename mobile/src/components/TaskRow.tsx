import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, Text, View } from 'react-native';

import { Row, styles } from '@/components/ui';
import { TaskRow as Task } from '@/lib/api';
import { day, time } from '@/lib/format';
import { useOutbox } from '@/lib/outbox';
import { colors, space } from '@/lib/theme';

/** A task with a tick box; ticking it marks it done on the server (and back). Tapping the text opens its lead. */
export function TaskItem({ task, last, onChanged, onError }: { task: Task; last?: boolean; onChanged: () => void; onError: (m: string) => void }) {
  const outbox = useOutbox();
  const [done, setDone] = useState(task.completedAt !== null);
  const [busy, setBusy] = useState(false);

  // Through the outbox: ticked offline, it stays ticked here and is sent when the connection's back.
  async function toggle() {
    if (!task.canEdit || busy) return;
    setBusy(true);
    setDone(!done);
    const outcome = await outbox.enqueue({
      kind: 'task_done', method: 'PUT', path: `/api/v1/tasks/${task.id}/done`, json: { done: !done },
      label: `${done ? 'Reopen' : 'Done'}: ${task.title}`,
    });
    if (outcome.status === 'failed') {
      setDone(done);
      onError(outcome.message);
    } else if (outcome.status === 'sent') onChanged();
    setBusy(false);
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
        {task.canEdit && !done && (
          <Ionicons name="create-outline" size={20} color={colors.muted} accessibilityLabel={`Edit ${task.title}`}
            onPress={() => router.push({
              pathname: '/edit-task',
              params: { id: task.id, title: task.title, notes: task.notes ?? '', due: task.dueLocal?.slice(0, 10) ?? '', priority: task.priority },
            })} />
        )}
      </View>
    </Row>
  );
}
